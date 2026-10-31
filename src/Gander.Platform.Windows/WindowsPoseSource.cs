using System.Threading.Channels;
using Gander.Core;
using Gander.Core.Platform;
using Gander.Core.Pose;
using Gander.Core.Runtime;
using Gander.Platform.Windows.Pose;

namespace Gander.Platform.Windows;

/// <summary>
/// Head pose from the live camera. Between <see cref="StartAsync"/> and <see cref="StopAsync"/> a dedicated thread
/// opens the chosen camera (Windows.Media.Capture, about 640x480), runs the face-landmark model on the frames the
/// <see cref="AdaptiveFrameRate"/> asks for, and writes status changes and <see cref="TimedPose"/> samples to a bounded
/// channel that drops its oldest entry when the reader is slow. Frames exist in memory only for the one inference.
/// </summary>
/// <remarks>
/// Status sequence: Starting, Running(device) at the first frame, Interrupted when no frame has come for 3 s (the
/// camera is released and reopened after the <see cref="RetryBackoff"/> wait), Failed(error) when it cannot continue
/// (no permission, no camera, camera removed, model missing), Stopped last. After Failed the stream stays open and the
/// camera released until <see cref="StopAsync"/>; the controller restarts after a device change.
/// <see cref="PoseSourceConfiguration.FrameRate"/> and <c>IncludeLandmarks</c> are not used here: the rate comes from
/// <see cref="SetDemand"/> (15 fps cap) and the landmark angles are not computed yet.
/// </remarks>
public sealed class WindowsPoseSource : IPoseSource, IDisposable
{
    internal const int ChannelCapacity = 64;
    private const int MaxConsecutiveInferenceFailures = 50;
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(5);

    private readonly ICameraCatalog _catalog;
    private readonly string _modelPath;
    private readonly Action<string>? _log;
    private readonly object _gate = new();
    private readonly object _rateGate = new();
    private readonly AdaptiveFrameRate _rate = new();
    private PoseDemand _demand = PoseDemand.Full;
    private bool _powerConstrained;
    private Run? _run;

    /// <param name="catalog">Used to pick the device, check permission and notice a camera that went away.</param>
    /// <param name="modelPath">Defaults to <c>models/face_landmark.onnx</c> next to the app.</param>
    /// <param name="log">Receives numbers-only lines when the face is tracked, lost or re-found. Null for none.</param>
    public WindowsPoseSource(ICameraCatalog catalog, string? modelPath = null, Action<string>? log = null)
    {
        _log = log;
        _catalog = catalog;
        _modelPath = modelPath ?? LandmarkModel.DefaultPath;
    }

    public Task<IAsyncEnumerable<PoseSourceEvent>> StartAsync(PoseSourceConfiguration configuration, CancellationToken cancellationToken = default)
    {
        Run run;
        lock (_gate)
        {
            if (_run is not null)
            {
                throw new InvalidOperationException("The pose source is already started.");
            }

            run = new Run(configuration, CancellationTokenSource.CreateLinkedTokenSource(cancellationToken));
            _run = run;
        }

        lock (_rateGate)
        {
            _rate.Reset();
            _rate.SetDemand(_demand);
            _rate.SetPowerConstrained(_powerConstrained);
        }

        run.Emit(new CaptureStatus.Starting());
        run.Task = Task.Factory.StartNew(() => RunLoop(run), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        return Task.FromResult(run.Reader.ReadAllAsync());
    }

    /// <summary>Stops capture, waits until the camera is released and the stream has completed (after a Stopped status).</summary>
    public async Task StopAsync()
    {
        Run? run;
        lock (_gate)
        {
            run = _run;
        }

        if (run is null)
        {
            return;
        }

        run.Cancel();

        // Bounded: if a driver stalls the capture thread, quitting still finishes (the process exit releases the camera).
        var finished = await Task.WhenAny(run.Task, Task.Delay(StopTimeout)).ConfigureAwait(false) == run.Task;
        if (!finished)
        {
            _log?.Invoke("The camera did not stop within " + StopTimeout.TotalSeconds.ToString("0") + " s; giving up waiting.");
        }

        lock (_gate)
        {
            if (ReferenceEquals(_run, run))
            {
                _run = null;
            }
        }

        if (finished)
        {
            run.DisposeToken(); // a stalled thread still uses the token, so it is left to the collector
        }
    }

    public void SetDemand(PoseDemand demand)
    {
        lock (_rateGate)
        {
            _demand = demand;
            _rate.SetDemand(demand);
        }
    }

    /// <summary>Battery saver or a thermal limit: lowers the analysis rate (see <see cref="AdaptiveFrameRate"/>).</summary>
    public void SetPowerConstrained(bool constrained)
    {
        lock (_rateGate)
        {
            _powerConstrained = constrained;
            _rate.SetPowerConstrained(constrained);
        }
    }

    public void Dispose() => StopAsync().GetAwaiter().GetResult();

    private bool Accept(double seconds)
    {
        lock (_rateGate)
        {
            return _rate.ShouldProcess(seconds);
        }
    }

    private void RunLoop(Run run)
    {
        var token = run.Token;
        CameraSource? camera = null;
        PosePipeline? pipeline = null;
        try
        {
            if (!TryCreatePipeline(out pipeline, out var failure))
            {
                run.Emit(new CaptureStatus.Failed(new CameraError(CameraErrorKind.ConfigurationFailed, failure)));
                WaitUntilStopped(token);
                return;
            }

            var supervisor = new CaptureSupervisor();
            var open = true;
            CameraDevice? device = null;
            var reportedRunning = false;
            double seenArrival = double.NegativeInfinity;
            long sequence = 0;
            var captureFailed = 0;
            var inferenceFailures = 0;

            while (!token.IsCancellationRequested)
            {
                if (open)
                {
                    open = false;
                    var outcome = OpenCamera(run.Configuration, supervisor, token, out camera, out device, out var terminal);
                    if (token.IsCancellationRequested)
                    {
                        break; // stopped while opening: the finally releases whatever opened
                    }

                    if (terminal is not null)
                    {
                        run.Emit(new CaptureStatus.Failed(terminal));
                        return; // finally releases; the stream waits for StopAsync below
                    }

                    if (outcome is { } interrupted)
                    {
                        run.Emit(interrupted);
                    }
                    else
                    {
                        pipeline.Reset();
                        lock (_rateGate)
                        {
                            _rate.Reset();
                            _rate.SetDemand(_demand);
                        }

                        reportedRunning = false;
                        seenArrival = double.NegativeInfinity;
                        sequence = 0;
                        Volatile.Write(ref captureFailed, 0);
                        var started = camera!;
                        started.CaptureFailed += _ => Volatile.Write(ref captureFailed, 1);
                    }
                }

                if (camera is null)
                {
                    token.WaitHandle.WaitOne(200);
                }
                else
                {
                    camera.WaitForFrame(250, token);
                }

                if (token.IsCancellationRequested)
                {
                    break;
                }

                var now = MonotonicClock.Seconds;
                if (camera is not null)
                {
                    if (camera.LastArrivalSeconds is { } arrival && arrival > seenArrival)
                    {
                        seenArrival = arrival;
                        supervisor.FrameArrived(arrival);
                        if (!reportedRunning)
                        {
                            reportedRunning = true;
                            run.Emit(new CaptureStatus.Running(device));
                        }
                    }

                    using var frame = camera.TryTake(ref sequence);
                    if (frame is not null)
                    {
                        inferenceFailures = Process(run, pipeline, frame, inferenceFailures);
                        if (inferenceFailures >= MaxConsecutiveInferenceFailures)
                        {
                            run.Emit(new CaptureStatus.Failed(new CameraError(CameraErrorKind.RuntimeFailure, "The face model failed on every frame.")));
                            return;
                        }
                    }
                }

                var action = Volatile.Read(ref captureFailed) == 1 ? supervisor.Interrupt(now) : supervisor.Tick(now);
                switch (action)
                {
                    case SupervisorAction.ReportInterrupted:
                        camera?.Dispose(); // release first, then say so
                        camera = null;
                        if (device is not null && !IsPresent(device))
                        {
                            run.Emit(new CaptureStatus.Failed(new CameraError(CameraErrorKind.DeviceUnavailable, "The camera was unplugged or turned off.")));
                            WaitUntilStopped(token);
                            return;
                        }

                        run.Emit(new CaptureStatus.Interrupted("No frames from the camera; trying again shortly."));
                        break;
                    case SupervisorAction.Rebuild:
                        open = true;
                        break;
                }
            }
        }
        catch (Exception exception)
        {
            run.Emit(new CaptureStatus.Failed(new CameraError(CameraErrorKind.RuntimeFailure, $"Camera capture stopped unexpectedly: {exception.Message}")));
        }
        finally
        {
            camera?.Dispose(); // the camera is fully released before the stream completes
            pipeline?.Dispose();
            if (!token.IsCancellationRequested)
            {
                // A terminal failure: keep the stream open (camera already released) until StopAsync.
                WaitUntilStopped(token);
            }

            run.Emit(new CaptureStatus.Stopped());
            run.Complete();
        }
    }

    /// <summary>
    /// Opens the camera. Returns null when streaming (or when a terminal error was set), else the Interrupted status
    /// for a failure worth retrying (the supervisor then holds the retry time).
    /// </summary>
    private CaptureStatus? OpenCamera(PoseSourceConfiguration configuration, CaptureSupervisor supervisor, CancellationToken token, out CameraSource? camera, out CameraDevice? device, out CameraError? terminal)
    {
        camera = null;
        device = null;
        terminal = null;

        if (_catalog.Authorization() is CameraAuthorization.Denied or CameraAuthorization.Restricted)
        {
            terminal = new CameraError(CameraErrorKind.NotAuthorized, "Camera access is turned off. Open Settings, Privacy and security, Camera, and allow desktop apps to use the camera.");
            return null;
        }

        device = _catalog.Resolve(configuration.CameraDeviceId);
        if (device is null)
        {
            terminal = new CameraError(CameraErrorKind.NoDevice, "No camera was found.");
            return null;
        }

        var source = new CameraSource(() => MonotonicClock.Seconds, Accept);

        // Bounded: a stalled driver neither hangs a stop nor keeps the camera on. A result that arrives after the
        // wait ended is disposed when it does, so a late open cannot leave the camera running.
        var deviceId = device.Id;
        var bounded = BoundedWait.Open(() => source.OpenAsync(deviceId), BoundedWait.CameraOpenTimeout, token, _ => source.Dispose());
        if (bounded.Outcome == BoundedOutcome.Cancelled)
        {
            return null;
        }

        var result = bounded.Outcome == BoundedOutcome.TimedOut
            ? new CameraOpenResult(CameraErrorKind.DeviceUnavailable, "The camera did not respond in time.")
            : bounded.Fault is { } fault
                ? new CameraOpenResult(CameraErrorKind.DeviceUnavailable, $"Could not start the camera: {fault.Message}")
                : bounded.Value;
        if (result.Kind is null)
        {
            camera = source;
            supervisor.Opened(MonotonicClock.Seconds);
            return null;
        }

        if (bounded.Outcome == BoundedOutcome.Completed)
        {
            source.Dispose();
        }

        var detail = result.Detail ?? "The camera could not be opened.";
        if (result.Kind == CameraErrorKind.DeviceUnavailable && IsPresent(device))
        {
            var delay = supervisor.OpenFailed(MonotonicClock.Seconds);
            return new CaptureStatus.Interrupted($"{detail} Trying again in {delay:0} s.");
        }

        terminal = new CameraError(result.Kind.Value, detail);
        return null;
    }

    private bool IsPresent(CameraDevice device) =>
        _catalog.AvailableDevices().Any(candidate => string.Equals(candidate.Id, device.Id, StringComparison.OrdinalIgnoreCase));

    private bool TryCreatePipeline(out PosePipeline pipeline, out string failure)
    {
        pipeline = null!;
        if (!File.Exists(_modelPath))
        {
            failure = $"The face model is missing: {_modelPath}. Reinstall Gander, or run scripts/fetch-pose-model.sh and rebuild.";
            return false;
        }

        try
        {
            var created = PosePipeline.TryCreate(_ => { }, out var reason, _modelPath);
            if (created is null)
            {
                failure = $"The face model could not be loaded: {reason}";
                return false;
            }

            created.StateLog = _log;
            pipeline = created;
            failure = string.Empty;
            return true;
        }
        catch (Exception exception)
        {
            failure = $"The face model could not be loaded: {exception.Message}";
            return false;
        }
    }

    /// <summary>Runs the pose pipeline on one frame and emits the sample. Returns the updated consecutive-failure count.</summary>
    private int Process(Run run, PosePipeline pipeline, CameraFrame frame, int failures)
    {
        HeadPose? pose = null;
        var failed = false;
        try
        {
            var result = pipeline.Process(frame);
            pose = result.Pose is { } found ? found with { FaceConfidence = Math.Clamp(result.LowPresence ? FaceRecovery.ColdThreshold : result.Presence, 0, 1) } : null;
        }
        catch (Exception)
        {
            failed = true;
        }

        lock (_rateGate)
        {
            _rate.Record(pose is not null, frame.TimeSeconds);
        }

        run.Emit(new PoseSourceEvent.Sample(new TimedPose(frame.TimeSeconds, pose)));
        return failed ? failures + 1 : 0;
    }

    private static void WaitUntilStopped(CancellationToken token) => token.WaitHandle.WaitOne();

    private sealed class Run
    {
        private readonly Channel<PoseSourceEvent> _channel = Channel.CreateBounded<PoseSourceEvent>(new BoundedChannelOptions(ChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });

        private readonly CancellationTokenSource _cancellation;

        internal Run(PoseSourceConfiguration configuration, CancellationTokenSource cancellation)
        {
            Configuration = configuration;
            _cancellation = cancellation;
        }

        internal PoseSourceConfiguration Configuration { get; }

        internal Task Task { get; set; } = Task.CompletedTask;

        internal CancellationToken Token => _cancellation.Token;

        internal ChannelReader<PoseSourceEvent> Reader => _channel.Reader;

        internal void Emit(CaptureStatus status) => Emit(new PoseSourceEvent.Status(status));

        internal void Emit(PoseSourceEvent value) => _channel.Writer.TryWrite(value);

        internal void Complete() => _channel.Writer.TryComplete();

        internal void Cancel() => _cancellation.Cancel();

        internal void DisposeToken() => _cancellation.Dispose();
    }
}
