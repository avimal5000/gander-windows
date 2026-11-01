using Gander.Core.Controller;
using Gander.Core.Platform;
using Gander.Core.Runtime;

namespace Gander.Core.Calibration;

/// <summary>
/// The Set Up Gander window's brain, UI-free (SPEC F20, D30, CONTRACT 6.5.3; Mac <c>AppModel+Setup</c>). It feeds the
/// <see cref="SetupFlow"/> its fact (is the camera allowed), keeps the camera running for the steps that need it, shows
/// the camera check's <see cref="FramingAdvice"/>, and runs a <see cref="CalibrationSession"/> for the calibrating step.
/// <para>
/// Threading: create it and call it on the main thread; <see cref="Changed"/> is raised there (through
/// <see cref="IMainThread"/>). The window binds to <see cref="Step"/>, <see cref="Framing"/>, <see cref="Calibration"/>
/// (progress and prompt), <see cref="Result"/> and <see cref="Message"/>.
/// </para>
/// </summary>
public sealed class SetupSession : IDisposable
{
    /// <summary>Seconds between camera-check hint refreshes, in sample time: at most five per second.</summary>
    public const double FramingRefreshInterval = 0.2;

    /// <summary>Seconds between permission re-reads while the "Needs you" step shows.</summary>
    public const double PermissionPollInterval = 1;

    private readonly TrackingController _controller;
    private readonly IMainThread _mainThread;
    private readonly IDelayScheduler _scheduler;
    private SetupFlow? _flow;
    private IScheduledAction? _permissionTimer;
    private bool _holdingCamera;
    private double? _lastFramingRefresh;
    private bool _disposed;

    /// <summary>Creates the session; see <see cref="CalibrationSession"/> for the parameters.</summary>
    public SetupSession(
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
        Calibration = new CalibrationSession(controller, mainThread, scheduler, clock, now, engine);
        Calibration.Changed += OnCalibrationChanged;
        _controller.StateChanged += OnControllerChanged;
        _controller.PoseObserved += OnPose;
    }

    /// <summary>Raised on the main thread when the step, the hint, the calibration state or the result changed.</summary>
    public event Action? Changed;

    /// <summary>The guided run inside the calibrating step (progress, prompt, message, result).</summary>
    public CalibrationSession Calibration { get; }

    /// <summary>True while the window is open (between <see cref="Open"/> and <see cref="Close"/>).</summary>
    public bool IsOpen => _flow is not null;

    /// <summary>The step that shows; null while closed.</summary>
    public SetupStep? Step => _flow?.Step;

    /// <summary>Latest camera-check hint; <see cref="FramingAdvice.NoFace"/> outside the camera check.</summary>
    public FramingAdvice Framing { get; private set; } = FramingAdvice.NoFace;

    /// <summary>The hint in words.</summary>
    public string FramingText => Framing.Text();

    /// <summary>True when the layout has enough monitors to calibrate.</summary>
    public bool CanStartCalibration => _controller.Layout.Displays.Count >= GateTestSession.MinimumDisplays;

    /// <summary>The verdict for the done step; null before a run finished.</summary>
    public SetupResult? Result { get; private set; }

    /// <summary>A line for the camera check (a cancelled or failed run, a missing second monitor); null otherwise.</summary>
    public CalibrationMessage? Message => Calibration.Message;

    /// <summary>Wording for the "Needs you" step: how to allow the camera in Windows Settings.</summary>
    public string NeedsYouText => _controller.PermissionPrompt == CameraPermissionPrompt.Restricted || _controller.Authorization == CameraAuthorization.Restricted
        ? SetupPresentation.CameraRestrictedMessage
        : SetupPresentation.CameraDeniedMessage;

    /// <summary>The window appeared: opens at the first step that applies, after the welcome when nothing is calibrated yet.</summary>
    public void Open()
    {
        if (_flow is not null || _disposed)
        {
            return;
        }

        _controller.RefreshEnvironment();
        Calibration.Reset();
        Result = null;
        _flow = new SetupFlow(CameraAllowed(), showsWelcome: _controller.Library.Profiles.Count == 0);
        FlowDidChange(SetupStep.Welcome, forced: true);
        Raise();
    }

    /// <summary>The window closed: cancels a run, lets the camera go unless Tracking needs it, forgets the flow.</summary>
    public void Close()
    {
        if (_flow is null)
        {
            return;
        }

        Calibration.Cancel(null);
        _flow = null;
        Result = null;
        Calibration.Reset();
        ResetFraming();
        StopPermissionTimer();
        ReleaseCamera();
        Raise();
    }

    /// <summary>"Get Started" on the welcome step. The permission is read again first.</summary>
    public void GetStarted()
    {
        if (_flow?.Step != SetupStep.Welcome)
        {
            return;
        }

        _controller.RefreshEnvironment();
        Mutate(f => f.GetStarted(CameraAllowed()));
    }

    /// <summary>The intro page that shows on the welcome step (0 when closed).</summary>
    public int IntroPage => _flow?.IntroPage ?? 0;

    /// <summary>"Next" on the intro; on the last page the permission is read again and the setup steps begin.</summary>
    public void IntroNext()
    {
        if (_flow?.Step != SetupStep.Welcome)
        {
            return;
        }

        _controller.RefreshEnvironment();
        Mutate(f => f.NextIntro(CameraAllowed()));
    }

    /// <summary>"Back" on the intro.</summary>
    public void IntroBack() => Mutate(f => f.BackIntro());

    /// <summary>"Skip intro": the permission is read again and the setup steps begin.</summary>
    public void IntroSkip()
    {
        if (_flow?.Step != SetupStep.Welcome)
        {
            return;
        }

        _controller.RefreshEnvironment();
        Mutate(f => f.SkipIntro(CameraAllowed()));
    }

    /// <summary>Re-reads the camera permission and moves the flow. The timer calls it each second on the "Needs you" step.</summary>
    public void Tick()
    {
        if (_flow?.IsPermissionStep != true)
        {
            return;
        }

        _controller.RefreshEnvironment();
        UpdateFlow();
    }

    /// <summary>Re-applies the camera fact to the flow.</summary>
    public void UpdateFlow() => Mutate(f => f.Update(CameraAllowed()));

    /// <summary>"Start calibration" on the camera check. Does nothing elsewhere.</summary>
    public async Task StartCalibrationAsync()
    {
        if (_flow?.Step != SetupStep.CameraCheck || Calibration.IsActive)
        {
            return;
        }

        Result = null;
        var run = Calibration.StartAsync();
        if (Calibration.IsActive)
        {
            Mutate(f => f.StartCalibration());
        }

        await run;
    }

    /// <summary>Cancel or Esc during the calibration: back to the camera check with a message.</summary>
    public void CancelCalibration() => Calibration.Cancel();

    /// <summary>"Calibrate again" on the done step: back to the camera check.</summary>
    public void CalibrateAgain()
    {
        Result = null;
        Calibration.Reset();
        Mutate(f => f.CalibrateAgain());
    }

    /// <summary>Tries the camera again after it failed to start (for example with another camera chosen).</summary>
    public async Task RetryCameraAsync()
    {
        if (_flow?.NeedsCamera == true && !_controller.CalibrationRunning && !Calibration.IsActive)
        {
            _holdingCamera = true;
            await _controller.SetCalibrationRunningAsync(true);
        }
    }

    /// <summary>"Start tracking" on the done step: the user's own choice, so it is saved; the flow closes.</summary>
    public async Task StartTrackingAsync()
    {
        // Tracking first, so the camera that is about to start for it is not stopped and started again.
        await _controller.SetTrackingEnabledAsync(true);
        Close();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Close();
        _disposed = true;
        Calibration.Changed -= OnCalibrationChanged;
        _controller.StateChanged -= OnControllerChanged;
        _controller.PoseObserved -= OnPose;
        Calibration.Dispose();
    }

    private bool CameraAllowed() =>
        _controller.Authorization is not (CameraAuthorization.Denied or CameraAuthorization.Restricted)
        && _controller.PermissionPrompt is null;

    private void Mutate(Action<SetupFlow> change)
    {
        if (_flow is null)
        {
            return;
        }

        var old = _flow.Step;
        var oldPage = _flow.IntroPage;
        change(_flow);
        if (_flow.Step != old)
        {
            FlowDidChange(old, forced: false);
            Raise();
        }
        else if (_flow.IntroPage != oldPage)
        {
            Raise();
        }
    }

    private void FlowDidChange(SetupStep old, bool forced)
    {
        if (_flow is null)
        {
            return;
        }

        if (_flow.Step == SetupStep.CameraCheck && (old != SetupStep.CameraCheck || forced))
        {
            ResetFraming();
        }

        SyncPermissionTimer();
        if (_flow.NeedsCamera)
        {
            if (!_controller.CalibrationRunning)
            {
                _holdingCamera = true;
                _ = HoldCameraAsync();
            }
        }
        else
        {
            ReleaseCamera();
        }
    }

    private async Task HoldCameraAsync()
    {
        try
        {
            await _controller.SetCalibrationRunningAsync(true);
        }
        catch (Exception)
        {
            // The controller reports camera trouble as state, never by throwing; a faulted task must not go unobserved.
        }
    }

    private void ReleaseCamera()
    {
        if (_holdingCamera && !Calibration.IsActive)
        {
            _holdingCamera = false;
            _ = HoldCameraReleaseAsync();
        }
    }

    private async Task HoldCameraReleaseAsync()
    {
        try
        {
            await _controller.SetCalibrationRunningAsync(false);
        }
        catch (Exception)
        {
            // See HoldCameraAsync.
        }
    }

    private void OnCalibrationChanged()
    {
        if (_flow is not null)
        {
            switch (Calibration.State)
            {
                case CalibrationState.Finished:
                    Result = Calibration.Result;
                    Mutate(f => f.CalibrationFinished());
                    break;
                case CalibrationState.Cancelled or CalibrationState.Aborted:
                    Mutate(f => f.CalibrationCancelled());
                    break;
                default:
                    break;
            }
        }

        Raise();
    }

    private void OnControllerChanged()
    {
        if (_flow is not null)
        {
            UpdateFlow();
        }
    }

    /// <summary>Offers a pose to the camera-check hint, at most every <see cref="FramingRefreshInterval"/> of sample time.</summary>
    private void OnPose(TimedPose sample)
    {
        if (_flow?.Step != SetupStep.CameraCheck)
        {
            return;
        }

        if (_lastFramingRefresh is { } last && sample.Timestamp >= last && sample.Timestamp - last < FramingRefreshInterval)
        {
            return;
        }

        _lastFramingRefresh = sample.Timestamp;
        var advice = FramingAdvices.For(sample.Pose);
        if (advice != Framing)
        {
            Framing = advice;
            Raise();
        }
    }

    private void ResetFraming()
    {
        _lastFramingRefresh = null;
        if (Framing != FramingAdvice.NoFace)
        {
            Framing = FramingAdvice.NoFace;
        }
    }

    private void SyncPermissionTimer()
    {
        if (_flow?.IsPermissionStep != true)
        {
            StopPermissionTimer();
            return;
        }

        if (_permissionTimer is null)
        {
            SchedulePermissionTick();
        }
    }

    private void SchedulePermissionTick() =>
        _permissionTimer = _scheduler.Schedule(PermissionPollInterval, () => _mainThread.Post(() =>
        {
            _permissionTimer = null;
            if (_disposed || _flow?.IsPermissionStep != true)
            {
                return;
            }

            Tick();
            SyncPermissionTimer();
        }));

    private void StopPermissionTimer()
    {
        _permissionTimer?.Cancel();
        _permissionTimer = null;
    }

    private void Raise() => _mainThread.Post(() =>
    {
        if (!_disposed)
        {
            Changed?.Invoke();
        }
    });
}
