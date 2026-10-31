using Gander.Core.Platform;
using Gander.Core.Runtime;

namespace Gander.Core.Controller;

// Camera lifecycle. The pose source runs only while Tracking is on and a calibration fits, or a calibration run needs
// it; every start and stop goes through one serial chain of operations so rapid toggling can never leave the camera in
// a state that disagrees with the latest request. Lock, sleep and session changes tear the source down through the Core
// SuspendTracker (privacy guarantee, SPEC D43).
public sealed partial class TrackingController
{
    /// <summary>Seconds after the last camera connect or disconnect notification before the device list is read again.</summary>
    public const double EnvironmentRefreshDelay = 0.5;

    private Task _cameraOperation = Task.CompletedTask;
    private CancellationTokenSource? _stream;
    private int _streamGeneration;
    private PoseSourceConfiguration? _activeConfiguration;
    private IScheduledAction? _environmentRefreshTimer;
    private IScheduledAction? _sourceRetryTimer;

    // True from the moment a running source reported a failure the camera may recover from (unplugged, in use) until
    // frames flow again. The session stays requested; the controller retries with RetryBackoff and the fallback may take over.
    private bool _cameraLost;

    // MARK: User actions

    /// <summary>
    /// Chooses the camera, persists the choice (null means default), and restarts capture if it is running. The
    /// calibration is looked up per camera, so the active calibration may change.
    /// </summary>
    public async Task SelectCameraAsync(string? id)
    {
        if (id == SelectedCameraId)
        {
            return;
        }

        // The user's own pick ends any stand-in; the saved choice is the only thing a fallback never writes.
        ClearCameraFallback();
        LostCameraId = null;
        SelectedCameraId = id;
        if (id is not null)
        {
            _env.Settings.Write(SettingsKeys.CameraDeviceId, id);
        }
        else
        {
            _env.Settings.Remove(SettingsKeys.CameraDeviceId);
        }

        RefreshPredictedCamera();
        RefreshActiveCalibration();
        Publish();
        if (!IsCameraActive)
        {
            // A camera check that could not start gets another try with the camera just chosen.
            if (WantsCamera)
            {
                await ReconcileCamera();
            }
        }
        else
        {
            await EnqueueCameraWork(RestartPoseSourceAsync);
        }

        Publish();
    }

    // MARK: Serial camera operations

    private Task EnqueueCameraWork(Func<Task> work)
    {
        var previous = _cameraOperation;
        var done = new TaskCompletionSource();
        _cameraOperation = done.Task;
        _ = RunAfter(previous, work, done);
        return done.Task;
    }

    private async Task RunAfter(Task previous, Func<Task> work, TaskCompletionSource done)
    {
        // Continue on the captured (main thread) context: with ConfigureAwait(false), a completion on a thread that has a
        // synchronization context would move the continuation to the thread pool, and the controller is main-thread only.
        await previous;
        try
        {
            await work();
        }
        catch (Exception ex)
        {
            Log($"Camera operation failed: {ex.GetType().Name}");
        }
        finally
        {
            done.SetResult();
        }
    }

    /// <summary>Queues a reconcile: start the camera if something wants it, stop it if nothing does.</summary>
    private Task ReconcileCamera()
    {
        if (_reconcileHold > 0)
        {
            _reconcileQueued = true;
            return Task.CompletedTask;
        }

        return EnqueueCameraWork(PerformCameraReconcileAsync);
    }

    // Camera work begins at once when nothing is ahead of it. A caller that is about to make more changes that the
    // reconcile must see (the layout change, which also begins the camera grace) holds it and flushes at the end.
    private int _reconcileHold;
    private bool _reconcileQueued;

    private void HoldReconcile() => _reconcileHold++;

    private void FlushReconcile()
    {
        _reconcileHold--;
        if (_reconcileHold == 0 && _reconcileQueued)
        {
            _reconcileQueued = false;
            _ = ReconcileCamera();
        }
    }

    private PoseSourceConfiguration DesiredConfiguration =>
        new(FallbackCamera?.Id ?? SelectedCameraId, GanderPolicy.DefaultFrameRate, IncludeLandmarks: false);

    private async Task PerformCameraReconcileAsync()
    {
        if (WantsCamera)
        {
            // The layout recovered (or something else wants the camera): the grace has nothing left to wait for.
            CancelCameraGrace();
            if (IsCameraActive)
            {
                // A running source is kept unless it was started with another configuration.
                if (_activeConfiguration != DesiredConfiguration)
                {
                    await RestartPoseSourceAsync();
                }

                return;
            }

            var calibrationWanted = CalibrationRunning;
            if (!await StartPoseSourceAsync())
            {
                CameraStartFailed(calibrationWanted);
            }
        }
        else if (IsCameraActive)
        {
            // A display change leaves the camera running for its grace (SPEC section 6); every other reason (Tracking
            // off, Pause, quit) stops it at once.
            if (IsCameraGraceActive && TrackingEnabled)
            {
                return;
            }

            CancelCameraGrace();
            ClearCameraFallback();
            await StopPoseSourceAsync();
        }

        Publish();
    }

    private async Task RestartPoseSourceAsync()
    {
        await StopPoseSourceAsync();
        if (!WantsCamera)
        {
            return;
        }

        var calibrationWanted = CalibrationRunning;
        if (!await StartPoseSourceAsync())
        {
            CameraStartFailed(calibrationWanted);
        }
    }

    // MARK: Start and stop

    /// <summary>
    /// Starts the pose source and begins consuming its stream. Returns false, with the reason surfaced in
    /// <see cref="PermissionPrompt"/> or <see cref="Status"/>, when capture could not start. While the session is
    /// suspended (lock, sleep) nothing starts, and it returns true: the resume starts it.
    /// </summary>
    private async Task<bool> StartPoseSourceAsync(bool retry = false)
    {
        Authorization = _env.Cameras.Authorization();
        switch (Authorization)
        {
            case CameraAuthorization.Denied:
                PermissionPrompt = CameraPermissionPrompt.Denied;
                return false;
            case CameraAuthorization.Restricted:
                PermissionPrompt = CameraPermissionPrompt.Restricted;
                return false;
            default:
                // Windows has no prompt for desktop apps; NotDetermined is tried and the source reports NotAuthorized.
                PermissionPrompt = null;
                break;
        }

        var directive = _suspend.CaptureRequested();
        var configuration = DesiredConfiguration;
        if (directive.Kind == SuspendDirectiveKind.Suspend)
        {
            _activeConfiguration = configuration;
            Status = new CaptureStatus.Suspended(directive.Reason!.Value);
            RunningDevice = null;
            ClearLiveReadings();
            StatusDidChange();
            return true;
        }

        Status = new CaptureStatus.Starting();
        if (!retry)
        {
            RunningDevice = null;
        }

        ClearLiveReadings();
        StatusDidChange();
        return await StartSourceAsync(configuration);
    }

    private async Task<bool> StartSourceAsync(PoseSourceConfiguration configuration)
    {
        try
        {
            var stream = await _env.PoseSource.StartAsync(configuration);
            _activeConfiguration = configuration;
            RebuildDecider();
            BeginConsuming(stream);

            // A start resets the source to the full rate: say what is needed, once.
            SyncPoseDemand(force: true);
            return true;
        }
        catch (CameraStartException ex)
        {
            HandleStartFailure(ex.Error);
        }
        catch (Exception ex)
        {
            HandleStartFailure(new CameraError(CameraErrorKind.ConfigurationFailed, ex.Message));
        }

        return false;
    }

    private void HandleStartFailure(CameraError error)
    {
        Log($"Pose source failed to start: {error.Kind}");
        if (!_cameraLost)
        {
            // A failed start leaves nothing running. A lost camera being retried stays requested until the retries give up.
            _suspend.CaptureStopped();
            _activeConfiguration = null;
        }

        if (error.Kind == CameraErrorKind.NotAuthorized)
        {
            // The pose source disagrees with our cached permission; trust it and show the instructions.
            Authorization = _env.Cameras.Authorization();
            PermissionPrompt = Authorization == CameraAuthorization.Restricted
                ? CameraPermissionPrompt.Restricted
                : CameraPermissionPrompt.Denied;
            Status = new CaptureStatus.Stopped();
        }
        else
        {
            Status = new CaptureStatus.Failed(error);
        }

        StatusDidChange();
    }

    /// <summary>Stops the source and tears down the stream consumer. Keeps a Failed status visible.</summary>
    private async Task StopPoseSourceAsync()
    {
        var running = InvalidateStream();
        _suspend.CaptureStopped();
        _activeConfiguration = null;
        RunningDevice = null;
        _appliedPoseDemand = null;
        _cameraLost = false;
        CancelSourceRetry();
        CancelCameraGrace();
        if (running)
        {
            await _env.PoseSource.StopAsync();
        }

        if (Status is not CaptureStatus.Failed)
        {
            Status = new CaptureStatus.Stopped();
        }

        HasFace = false;
        _debounce.Reset();
        ResetTracking();
        StatusDidChange();
    }

    /// <summary>Drops the stream consumer; events already queued from it are ignored. Returns whether a source was started.</summary>
    private bool InvalidateStream()
    {
        var cts = _stream;
        _stream = null;
        _streamGeneration++;
        cts?.Cancel();
        return cts is not null;
    }

    /// <summary>The camera status changed: its device may change which calibration applies, and whether the focused display needs polling.</summary>
    private void StatusDidChange()
    {
        RefreshActiveCalibration();
        SyncTrackingActivity();
    }

    /// <summary>
    /// Resets intent state after a start attempt failed. The Tracking switch is turned off in memory (never in the
    /// settings) except when the camera is blocked by its permission: then it stays on, so the status says "Camera
    /// access needed" and tracking resumes once access is granted. The one other exception is a restart that the
    /// camera fallback began.
    /// </summary>
    private void CameraStartFailed(bool calibrationWanted)
    {
        // A restart the fallback began that failed only because the camera is not ready keeps Tracking on.
        if (!calibrationWanted && RecoverFromFailedFallbackStart())
        {
            return;
        }

        _fallbackRestart = null;
        if (PermissionPrompt is null)
        {
            TrackingEnabled = false;
        }

        if (calibrationWanted)
        {
            CalibrationRunning = false;
            SyncTrackingActivity();
        }
    }

    // MARK: Stream consumption

    private void BeginConsuming(IAsyncEnumerable<PoseSourceEvent> stream)
    {
        _streamGeneration++;
        var generation = _streamGeneration;
        var cts = new CancellationTokenSource();
        _stream = cts;
        _ = ConsumeAsync(stream, generation, cts.Token);
    }

    private async Task ConsumeAsync(IAsyncEnumerable<PoseSourceEvent> stream, int generation, CancellationToken token)
    {
        try
        {
            await foreach (var sourceEvent in stream.WithCancellation(token).ConfigureAwait(false))
            {
                var captured = sourceEvent;
                Post(() => HandleStreamEvent(captured, generation));
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception)
        {
            // A faulted stream ends like a finished one: capture is over.
        }

        Post(() => StreamDidEnd(generation));
    }

    /// <summary>Applies an event from the stream consumed as <paramref name="generation"/>, unless that stream was stopped or replaced.</summary>
    private void HandleStreamEvent(PoseSourceEvent sourceEvent, int generation)
    {
        if (generation != _streamGeneration || _stream is null)
        {
            return;
        }

        switch (sourceEvent)
        {
            case PoseSourceEvent.Sample sample:
                HandleSample(sample.Pose);
                break;
            case PoseSourceEvent.Status status:
                HandleStatus(status.Value);
                break;
        }
    }

    /// <summary>The stream finished without us asking: capture is over.</summary>
    private void StreamDidEnd(int generation)
    {
        if (generation != _streamGeneration || _stream is null)
        {
            return;
        }

        _stream = null;
        _activeConfiguration = null;
        RunningDevice = null;
        _appliedPoseDemand = null;
        _suspend.CaptureStopped();
        CancelCameraGrace();
        ClearCameraFallback();
        if (Status is CaptureStatus.Running or CaptureStatus.Starting)
        {
            Status = new CaptureStatus.Stopped();
        }

        ClearLiveReadings();
        TrackingEnabled = false;
        StatusDidChange();
    }

    private void HandleSample(TimedPose sample)
    {
        // A face shows at once; "no face" only after half a second without one (Mac CONTRACT 6.5.5 item 8).
        var face = _debounce.Update(sample.Pose is not null, sample.Timestamp);
        if (face != HasFace)
        {
            HasFace = face;
        }

        _backoff.FramesFlowed();
        _diagnostics.RecordSample(sample.Pose, _env.Now());
        PoseObserved?.Invoke(sample);
        ProcessTracking(sample);
        MaybeLogDiagnostics();
    }

    /// <summary>Forgets the latest pose, the debounce, and the tracking loop's dwell and focus (camera stopped, restarted, or interrupted).</summary>
    private void ClearLiveReadings()
    {
        HasFace = false;
        _debounce.Reset();
        ResetTracking();
    }

    /// <summary>One log line for a camera status report: the state, and its error kind or detail. No window or image data.</summary>
    internal static string StatusLogLine(CaptureStatus status) => status switch
    {
        CaptureStatus.Failed failed => $"Camera status: Failed ({failed.Error.Kind})",
        CaptureStatus.Interrupted interrupted => $"Camera status: Interrupted ({interrupted.Detail})",
        CaptureStatus.Suspended suspended => $"Camera status: Suspended ({suspended.Reason})",
        _ => $"Camera status: {status.GetType().Name}",
    };

    private void HandleStatus(CaptureStatus newStatus)
    {
        Log(StatusLogLine(newStatus));
        var wasSuspended = Status is CaptureStatus.Suspended;
        if (newStatus is CaptureStatus.Failed failed && IsRecoverableLoss(failed.Error))
        {
            EnterCameraLost(failed.Error);
            return;
        }

        Status = newStatus;
        if (newStatus is CaptureStatus.Running running)
        {
            if (running.Device is { } device)
            {
                RunningCameraName = device.Name;
                RunningDevice = device;
            }

            _cameraLost = false;
            CancelSourceRetry();
        }
        else
        {
            ClearLiveReadings();
        }

        StatusDidChange();

        // Camera changes were not read while the camera was suspended (asleep, locked): read them once now.
        if (wasSuspended && newStatus is not CaptureStatus.Suspended)
        {
            RefreshEnvironment();
        }

        if (newStatus is CaptureStatus.Failed)
        {
            // An automatic turn-off: the saved choice stays, so the next launch tries again.
            TrackingEnabled = false;
            StatusDidChange();
            _ = ReconcileCamera();
        }
    }

    // MARK: Lost camera (the source failed while running)

    // CONTRACT section 3 says a camera that disappears reports Failed(DeviceUnavailable). A source that was running and
    // fails that way is an interruption, not the end of Tracking (SPEC D46): the controller waits for the camera with
    // RetryBackoff, and the fallback may take over after 5 s.
    private bool IsRecoverableLoss(CameraError error) =>
        error.IsCameraNotReady() && TrackingEnabled && !CalibrationRunning && (RunningDevice is not null || _cameraLost);

    private void EnterCameraLost(CameraError error)
    {
        InvalidateStream();
        _appliedPoseDemand = null;
        _cameraLost = true;
        var devices = _env.Cameras.AvailableDevices();
        if (!devices.SequenceEqual(Devices))
        {
            Devices = devices;
            RememberCameraNames();
        }

        var missing = RunningDevice is { } device && Devices.All(d => d.Id != device.Id);
        Status = new CaptureStatus.Interrupted(missing ? "Camera disconnected" : (error.Detail.Length > 0 ? error.Detail : "Camera interrupted"));
        ClearLiveReadings();
        StatusDidChange();
        _ = EnqueueCameraWork(() => _env.PoseSource.StopAsync());
        ScheduleSourceRetry();
    }

    private void ScheduleSourceRetry()
    {
        _sourceRetryTimer?.Cancel();
        _sourceRetryTimer = _scheduler.Schedule(_backoff.NextOpenFailureDelay(), () =>
        {
            _sourceRetryTimer = null;
            RetrySourceNow();
        });
    }

    private void CancelSourceRetry()
    {
        _sourceRetryTimer?.Cancel();
        _sourceRetryTimer = null;
    }

    private void RetrySourceNow()
    {
        CancelSourceRetry();
        if (_cameraLost && IsCameraActive)
        {
            _ = EnqueueCameraWork(RetrySourceAsync);
        }
    }

    private async Task RetrySourceAsync()
    {
        if (!_cameraLost || !IsCameraActive || !WantsCamera || _suspend.IsSuspended)
        {
            return;
        }

        if (!await StartPoseSourceAsync(retry: true))
        {
            if (Status is CaptureStatus.Failed { Error: var error } && error.IsCameraNotReady())
            {
                Status = new CaptureStatus.Interrupted(error.Detail.Length > 0 ? error.Detail : "Camera disconnected");
                StatusDidChange();
                ScheduleSourceRetry();
            }
            else
            {
                CameraStartFailed(false);
            }
        }
    }

    // MARK: Device list

    private void RememberCameraNames()
    {
        foreach (var device in Devices)
        {
            _knownCameraNames[device.Id] = device.Name;
        }
    }

    private void OnDevicesChanged() => Post(CameraDevicesDidChange);

    /// <summary>
    /// A camera connected or disconnected. A burst of these (a hub or dock re-attaching) becomes one refresh of the
    /// device list 0.5 s after the last, and none while the camera is suspended, where nothing needs the list: one
    /// refresh runs when the camera leaves that state.
    /// </summary>
    private void CameraDevicesDidChange()
    {
        _environmentRefreshTimer?.Cancel();
        _environmentRefreshTimer = null;
        if (Status is CaptureStatus.Suspended)
        {
            return;
        }

        _environmentRefreshTimer = _scheduler.Schedule(EnvironmentRefreshDelay, () =>
        {
            _environmentRefreshTimer = null;
            if (Status is not CaptureStatus.Suspended)
            {
                RefreshEnvironment();
            }
        });
    }

    /// <summary>Re-reads the camera list and permission. Call when a menu or window appears; also runs when a camera connects or disconnects.</summary>
    public void RefreshEnvironment()
    {
        var previousAuthorization = Authorization;
        var devices = _env.Cameras.AvailableDevices();
        if (!devices.SequenceEqual(Devices))
        {
            Devices = devices;
            DeviceListDidChange();
        }

        RememberCameraNames();
        RefreshPredictedCamera();
        Authorization = _env.Cameras.Authorization();
        if (Authorization == CameraAuthorization.Authorized && PermissionPrompt is not null)
        {
            PermissionPrompt = null;
        }

        // The camera that capture would pick may have changed with the device list.
        RefreshActiveCalibration();
        SyncCameraFallback();

        // Access granted in Windows Settings: a Tracking switch left on, blocked, starts the camera now.
        if (Authorization == CameraAuthorization.Authorized && previousAuthorization != CameraAuthorization.Authorized
            && WantsCamera && !IsCameraActive)
        {
            _ = ReconcileCamera();
        }

        // A camera that was lost may be back: try it now instead of waiting out the backoff.
        if (_cameraLost && WantedCameraIsConnected)
        {
            RetrySourceNow();
        }

        Publish();
    }

    private bool WantedCameraIsConnected
    {
        get
        {
            var id = FallbackCamera?.Id ?? SelectedCameraId;
            return id is null ? Devices.Count > 0 : Devices.Any(d => d.Id == id);
        }
    }

    // MARK: Session (lock, sleep, user switch)

    private void OnSessionEvent(SessionEvent sessionEvent) => Post(() => HandleSessionEvent(sessionEvent));

    private void HandleSessionEvent(SessionEvent sessionEvent)
    {
        SuspendEvent? mapped = sessionEvent switch
        {
            SessionEvent.ScreenLocked => SuspendEvent.Begin(SuspendReason.ScreenLocked),
            SessionEvent.ScreenUnlocked => SuspendEvent.End(SuspendReason.ScreenLocked),
            SessionEvent.SystemSleeping => SuspendEvent.Begin(SuspendReason.SystemSleep),
            SessionEvent.SystemWoke => SuspendEvent.End(SuspendReason.SystemSleep),
            SessionEvent.DisplaysSleeping => SuspendEvent.Begin(SuspendReason.DisplaysAsleep),
            SessionEvent.DisplaysWoke => SuspendEvent.End(SuspendReason.DisplaysAsleep),
            SessionEvent.SessionInactive => SuspendEvent.Begin(SuspendReason.SessionInactive),
            SessionEvent.SessionActive => SuspendEvent.End(SuspendReason.SessionInactive),
            _ => null,
        };
        if (mapped is { } suspendEvent)
        {
            ApplySuspendDirective(_suspend.Apply(suspendEvent, _env.IsMainDisplayAsleep));
        }
    }

    private void ApplySuspendDirective(SuspendDirective directive)
    {
        switch (directive.Kind)
        {
            case SuspendDirectiveKind.Suspend:
                // Reported at once; the source is torn down right behind it. Late events of the old stream are ignored.
                InvalidateStream();
                _appliedPoseDemand = null;
                CancelSourceRetry();
                Status = new CaptureStatus.Suspended(directive.Reason!.Value);
                ClearLiveReadings();
                StatusDidChange();
                _ = EnqueueCameraWork(() => _env.PoseSource.StopAsync());
                break;
            case SuspendDirectiveKind.ReasonChanged:
                Status = new CaptureStatus.Suspended(directive.Reason!.Value);
                StatusDidChange();
                break;
            case SuspendDirectiveKind.Resume:
                _ = EnqueueCameraWork(ResumeAfterSuspendAsync);
                break;
        }
    }

    private async Task ResumeAfterSuspendAsync()
    {
        if (_suspend.IsSuspended || !_suspend.IsCaptureRequested || _stream is not null)
        {
            return;
        }

        _cameraLost = false;
        if (!WantsCamera)
        {
            await StopPoseSourceAsync();
            return;
        }

        var calibrationWanted = CalibrationRunning;
        if (!await StartPoseSourceAsync())
        {
            CameraStartFailed(calibrationWanted);
        }

        RefreshEnvironment();
    }
}
