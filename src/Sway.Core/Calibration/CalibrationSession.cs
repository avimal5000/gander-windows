using Sway.Core.Controller;
using Sway.Core.Platform;
using Sway.Core.Runtime;

namespace Sway.Core.Calibration;

/// <summary>Where a <see cref="CalibrationSession"/> is.</summary>
public enum CalibrationState
{
    /// <summary>Not running (never started, or <see cref="CalibrationSession.Reset"/>, or a start refused before it began).</summary>
    Idle,

    /// <summary>The camera is starting; the guided run begins when it reports running.</summary>
    StartingCamera,

    /// <summary>The guided run is in progress; see <see cref="CalibrationSession.Progress"/>.</summary>
    Running,

    /// <summary>The run completed; see <see cref="CalibrationSession.Result"/> (saved, or failed with a reason).</summary>
    Finished,

    /// <summary>The user cancelled (or the monitors changed); see <see cref="CalibrationSession.Message"/>. Nothing saved.</summary>
    Cancelled,

    /// <summary>The run was aborted with an error (camera lost, did not start); see <see cref="CalibrationSession.Message"/>. Nothing saved.</summary>
    Aborted,
}

/// <summary>A line the window shows beside the run: info (cancelled) or an error.</summary>
public sealed record CalibrationMessage(bool IsError, string Text);

/// <summary>
/// The guided calibration (SPEC D30, F20), UI-free: two rounds over the current monitors in layout order, each look 1.5 s
/// of settling and 2.5 s of hold, with a prompt for the monitor to look at. Samples come from the live pose stream
/// (<see cref="TrackingController.PoseObserved"/>). On success it fits a Vision-angles profile with the Core
/// <see cref="ProfileFitter"/>, saves it through <see cref="TrackingController.SaveToLibrary"/> and describes how well the
/// monitors separate (<see cref="SeparabilityAnalyzer"/>). Mac: <c>AppModel+Gate</c> with the calibration purpose plus
/// <c>finishCalibration</c>.
/// <para>
/// Threading: create it and call it on the main thread. <see cref="Changed"/> is raised on the main thread (through
/// <see cref="IMainThread"/>) when the state, progress, message or result changes; during a run that is about ten times
/// a second (the progress ticks), never per pose sample unless the step or phase changed.
/// </para>
/// <para>
/// Camera: the session turns the controller's calibration hold on if it is off and turns it off again when the run ends.
/// If the host (the setup window) already holds it, the session leaves it as it found it.
/// </para>
/// </summary>
public sealed class CalibrationSession : IDisposable
{
    /// <summary>Seconds between progress ticks.</summary>
    public const double TickInterval = 0.1;

    /// <summary>Seconds the camera gets to report running before the run is aborted.</summary>
    public const double CameraStartTimeout = 10;

    private readonly TrackingController _controller;
    private readonly IMainThread _mainThread;
    private readonly IDelayScheduler _scheduler;
    private readonly Func<double> _clock;
    private readonly Func<DateTimeOffset> _now;
    private readonly ISwitchEngineMaking _engine;
    private GateTestSession? _session;
    private IScheduledAction? _tick;
    private double _cameraDeadline;
    private bool _acquiredCamera;
    private string _layoutFingerprint = string.Empty;
    private int _runId;
    private int _lastStep;
    private GatePhase? _lastPhase;
    private bool _disposed;

    /// <summary>Creates the session.</summary>
    /// <param name="controller">The tracking controller that owns the camera and the library.</param>
    /// <param name="mainThread">The controller's main thread; the tick and <see cref="Changed"/> go through it.</param>
    /// <param name="scheduler">Runs the progress tick.</param>
    /// <param name="clock">Monotonic seconds.</param>
    /// <param name="now">Wall clock for the trace's creation time; defaults to the system clock.</param>
    /// <param name="engine">Fits the profile; defaults to <see cref="PoseSwitchEngine"/>.</param>
    public CalibrationSession(
        TrackingController controller,
        IMainThread mainThread,
        IDelayScheduler scheduler,
        Func<double> clock,
        Func<DateTimeOffset>? now = null,
        ISwitchEngineMaking? engine = null)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _mainThread = mainThread ?? throw new ArgumentNullException(nameof(mainThread));
        _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _engine = engine ?? new PoseSwitchEngine();
        _controller.PoseObserved += OnPose;
        _controller.StateChanged += OnControllerChanged;
    }

    /// <summary>Raised on the main thread when anything below changed.</summary>
    public event Action? Changed;

    /// <summary>Where the session is.</summary>
    public CalibrationState State { get; private set; } = CalibrationState.Idle;

    /// <summary>True while the camera starts or the run is in progress.</summary>
    public bool IsActive => State is CalibrationState.StartingCamera or CalibrationState.Running;

    /// <summary>The monitor to look at and the countdown; null unless <see cref="State"/> is Running.</summary>
    public GateProgress? Progress { get; private set; }

    /// <summary>The prompt for the current look ("Look at Left"); null unless Running.</summary>
    public string? Prompt => Progress is { } p ? SetupPresentation.LookAt(_controller.DisplayName(p.Display.Id)) : null;

    /// <summary>Why the run did not finish, or a problem found before it began; null otherwise.</summary>
    public CalibrationMessage? Message { get; private set; }

    /// <summary>The verdict after a finished run: quality, words, and whether a calibration was saved.</summary>
    public SetupResult? Result { get; private set; }

    /// <summary>The profile the last finished run saved; null if none.</summary>
    public CalibrationProfile? SavedProfile { get; private set; }

    /// <summary>Completes when the camera has been handed back after the last run ended (tests await it).</summary>
    public Task CameraReleased { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Starts the guided run: checks the monitors, makes sure the camera runs, then begins the first look. Ignored while
    /// a run is going. With fewer than two monitors nothing starts and <see cref="Message"/> says so.
    /// </summary>
    public async Task StartAsync()
    {
        if (IsActive || _disposed)
        {
            return;
        }

        Message = null;
        Result = null;
        SavedProfile = null;
        Progress = null;
        var layout = _controller.Layout;
        if (layout.Displays.Count < GateTestSession.MinimumDisplays)
        {
            State = CalibrationState.Idle;
            Message = new CalibrationMessage(true, SetupPresentation.NeedsSecondDisplay);
            Raise();
            return;
        }

        _layoutFingerprint = layout.Fingerprint;
        _runId++;
        var run = _runId;
        State = CalibrationState.StartingCamera;
        _cameraDeadline = _clock() + CameraStartTimeout;
        _acquiredCamera = !_controller.CalibrationRunning;
        StartTick();
        Raise();
        await _controller.SetCalibrationRunningAsync(true);
        if (run != _runId || State != CalibrationState.StartingCamera)
        {
            return;
        }

        Evaluate();
    }

    /// <summary>Cancels the run (Cancel button, Esc, closing the window). Nothing is saved. Does nothing when no run is going.</summary>
    public void Cancel(string? reason = SetupPresentation.CalibrationCancelled)
    {
        if (!IsActive)
        {
            return;
        }

        End(CalibrationState.Cancelled, reason is null ? null : new CalibrationMessage(false, reason));
    }

    /// <summary>Back to <see cref="CalibrationState.Idle"/>, forgetting the message and result. Ignored while a run is going.</summary>
    public void Reset()
    {
        if (IsActive || (State == CalibrationState.Idle && Message is null && Result is null))
        {
            return;
        }

        State = CalibrationState.Idle;
        Message = null;
        Result = null;
        SavedProfile = null;
        Raise();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (IsActive)
        {
            End(CalibrationState.Cancelled, null);
        }

        _disposed = true;
        _controller.PoseObserved -= OnPose;
        _controller.StateChanged -= OnControllerChanged;
        StopTick();
    }

    private void OnPose(TimedPose sample)
    {
        if (State != CalibrationState.Running || _session is null)
        {
            return;
        }

        _session.Ingest(sample);
        if (_session.Phase == GateSessionPhase.Finished)
        {
            Complete();
            return;
        }

        var progress = _session.Progress;
        if (progress?.StepNumber != _lastStep || progress?.Phase != _lastPhase)
        {
            RefreshProgress();
            Raise();
        }
    }

    private void OnControllerChanged()
    {
        if (IsActive)
        {
            Evaluate();
        }
    }

    private void StartTick()
    {
        StopTick();
        var run = _runId;
        _tick = _scheduler.Schedule(TickInterval, () => _mainThread.Post(() => Tick(run)));
    }

    private void StopTick()
    {
        _tick?.Cancel();
        _tick = null;
    }

    private void Tick(int run)
    {
        if (_disposed || run != _runId || !IsActive)
        {
            return;
        }

        StartTick();
        Evaluate();
    }

    /// <summary>Reacts to the camera and the monitors; advances the run.</summary>
    private void Evaluate()
    {
        switch (State)
        {
            case CalibrationState.StartingCamera:
                if (_controller.Status is CaptureStatus.Failed failed)
                {
                    End(CalibrationState.Aborted, Error($"The camera failed: {CaptureText.UserMessage(failed.Error)}."));
                }
                else if (_controller.Status is CaptureStatus.Running)
                {
                    Begin();
                }
                else if (!_controller.CalibrationRunning)
                {
                    End(CalibrationState.Aborted, Error("The camera could not be started. Check that no other app is using it, then try again."));
                }
                else if (_clock() >= _cameraDeadline)
                {
                    End(CalibrationState.Aborted, Error("The camera did not start in time. Check that no other app is using it, then try again."));
                }

                break;
            case CalibrationState.Running:
                if (_controller.Status is not CaptureStatus.Running)
                {
                    End(
                        CalibrationState.Aborted,
                        Error($"The camera stopped delivering frames ({CaptureText.Summary(_controller.Status).ToLowerInvariant()}). {SetupPresentation.TryAgain}"));
                }
                else if (_controller.Layout.Fingerprint != _layoutFingerprint)
                {
                    End(CalibrationState.Cancelled, new CalibrationMessage(false, "The monitor arrangement changed, so the calibration was cancelled."));
                }
                else
                {
                    _session!.Advance();
                    if (_session.Phase == GateSessionPhase.Finished)
                    {
                        Complete();
                    }
                    else
                    {
                        RefreshProgress();
                        Raise();
                    }
                }

                break;
            default:
                break;
        }
    }

    private void Begin()
    {
        _session = new GateTestSession(_controller.Layout, _controller.CurrentCameraName, GateTestConfiguration.Calibration, _clock);
        _session.Start();
        State = CalibrationState.Running;
        RefreshProgress();
        Raise();
    }

    private void RefreshProgress()
    {
        Progress = _session?.Progress;
        _lastStep = Progress?.StepNumber ?? 0;
        _lastPhase = Progress?.Phase;
    }

    private static CalibrationMessage Error(string text) => new(true, text);

    /// <summary>Ends a run that did not finish: nothing is saved, the camera goes back.</summary>
    private void End(CalibrationState state, CalibrationMessage? message)
    {
        _session?.Cancel();
        _session = null;
        Progress = null;
        StopTick();
        State = state;
        Message = message;
        ReleaseCamera();
        Raise();
    }

    /// <summary>The run reached the end: release the camera, fit, save, and describe the result.</summary>
    private void Complete()
    {
        var trace = _session!.MakeTrace(_now());
        _session = null;
        Progress = null;
        StopTick();
        State = CalibrationState.Finished;
        ReleaseCamera();
        Result = trace is null ? SetupResult.Failed("Calibration did not finish.") : FitAndSave(trace);
        Raise();
    }

    private void ReleaseCamera()
    {
        if (_acquiredCamera)
        {
            _acquiredCamera = false;
            CameraReleased = _controller.SetCalibrationRunningAsync(false);
        }
    }

    private SetupResult FitAndSave(LabeledPoseTrace trace)
    {
        var layout = trace.Layout ?? _controller.Layout;
        var names = DisplayLabeler.Labels(layout);
        string Name(DisplayId id) => names.TryGetValue(id, out var name) ? name : _controller.DisplayName(id);
        CalibrationProfile profile;
        try
        {
            profile = _engine.FitProfile(trace, PoseFeatureSet.VisionAngles, null);
        }
        catch (CalibrationException ex)
        {
            return SetupResult.Failed(CalibrationFailureText(ex));
        }

        var missing = layout.Displays.Select(d => d.Id).Where(id => !profile.DisplayIds.Contains(id)).ToList();
        if (missing.Count > 0)
        {
            return SetupResult.MissingDisplays(missing.Select(Name).ToList());
        }

        _controller.SaveToLibrary(profile);
        SavedProfile = profile;
        var analysis = SeparabilityAnalyzer.Analyze(Flatten(trace));
        return SetupResult.FromAnalysis(analysis, label => Name(new DisplayId(label)));
    }

    /// <summary>Why a fit failed, in words for the user.</summary>
    public static string CalibrationFailureText(CalibrationException ex) => ex.Failure switch
    {
        CalibrationFailure.TooFewDisplays =>
            "Not enough face samples on at least two monitors to calibrate. Keep your face in view of the camera and try again.",
        _ => "Gander could not read your head angles from the camera. Try again.",
    };

    /// <summary>The trace as flat labeled samples for the separability analysis (label = display id, round from the segment).</summary>
    internal static List<PoseSample> Flatten(LabeledPoseTrace trace)
    {
        var samples = new List<PoseSample>();
        foreach (var segment in trace.Segments)
        {
            if (segment.DisplayId is not { } id)
            {
                continue;
            }

            foreach (var s in segment.Samples)
            {
                samples.Add(s.Pose is { } p
                    ? new PoseSample(s.Timestamp, p.Yaw, p.Pitch, p.Roll, id.RawValue, segment.Round)
                    : new PoseSample(s.Timestamp, 0, 0, 0, id.RawValue, segment.Round, false));
            }
        }

        return samples;
    }

    private void Raise() => _mainThread.Post(() =>
    {
        if (!_disposed)
        {
            Changed?.Invoke();
        }
    });
}
