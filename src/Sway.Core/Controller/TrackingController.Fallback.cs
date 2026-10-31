using Sway.Core.Platform;
using Sway.Core.Runtime;

namespace Sway.Core.Controller;

// The USB camera fallback (SPEC D46, Mac CONTRACT 6.5.5 item 7). When the camera the user chose is unplugged and
// capture stays interrupted for 5 s, Sway restarts the pose source on a connected camera that has a calibration for the
// current displays (the built-in first), and restarts it on the chosen camera once that is connected again. With no
// calibrated camera to use, Sway turns Tracking off in memory (the saved choice stays, as for a camera failure, D23) and
// the status names the missing camera until it is connected again; the user turns Tracking back on, it never resumes by
// itself. The saved camera choice is never touched: the stand-in lives in FallbackCamera and in the pose source
// configuration alone. Nothing changes while a calibration runs or while the camera is suspended.
public sealed partial class TrackingController
{
    /// <summary>How long the chosen camera may stay unplugged, with capture interrupted, before a stand-in takes over, in seconds.</summary>
    public const double CameraFallbackDelay = 5;

    private IScheduledAction? _cameraLossTimer;
    private bool _cameraLossElapsed;
    private FallbackRestart? _fallbackRestart;
    private int _fallbackRestartSerial;
    private IScheduledAction? _fallbackRetryTimer;
    private bool _fallbackSwitchBackDeferred;

    /// <summary>
    /// The camera the user chose, when it was the one running in this session and is no longer connected. Null for a
    /// default choice, and when capture runs a camera it picked on its own.
    /// </summary>
    private CameraDevice? UnpluggedChosenCamera =>
        IsCameraActive && SelectedCameraId is { } id && RunningDevice is { } device && device.Id == id
        && Devices.All(d => d.Id != id)
            ? device
            : null;

    private bool IsChosenCameraConnected => SelectedCameraId is not { } id || Devices.Any(d => d.Id == id);

    private CameraDevice? UnpluggedStandIn =>
        IsCameraActive && FallbackCamera is { } standIn && RunningDevice is { } device && device.Id == standIn.Id
        && Devices.All(d => d.Id != standIn.Id)
            ? device
            : null;

    // Capture is interrupted (or lost) and the camera it runs, the chosen one or its stand-in, is the one that went missing.
    private bool IsRunningCameraLost =>
        (Status is CaptureStatus.Interrupted || _cameraLost) && (UnpluggedChosenCamera is not null || UnpluggedStandIn is not null);

    // A start that the fallback began failed because the camera was not ready: nothing runs, Tracking is still on, and
    // the next device change (or the retry timer) tries again.
    private bool IsRetryPending => _cameraLossElapsed && !IsCameraActive && TrackingEnabled;

    /// <summary>
    /// What the status names as disconnected. With Tracking on, the chosen camera while capture waits for it. With
    /// Tracking off, the chosen camera that Tracking was turned off for, until it is connected again.
    /// </summary>
    public string? DisconnectedCameraName
    {
        get
        {
            if (!TrackingEnabled)
            {
                if (LostCameraId is not { } id || id != SelectedCameraId || Devices.Any(d => d.Id == id))
                {
                    return null;
                }

                return _knownCameraNames.GetValueOrDefault(id) ?? "The chosen camera";
            }

            return FallbackCamera is null ? UnpluggedChosenCamera?.Name : null;
        }
    }

    /// <summary>A connected camera other than the chosen one that has a calibration for the current displays: the built-in if it qualifies, else the first listed.</summary>
    private CameraDevice? FallbackCandidate()
    {
        var usable = Devices
            .Where(d => d.Id != SelectedCameraId && Library.Resolve(Layout, d.Name) is not null)
            .ToList();
        return usable.FirstOrDefault(d => d.IsBuiltIn) ?? usable.FirstOrDefault();
    }

    // MARK: Keeping in step

    /// <summary>
    /// Brings the fallback in line with the camera state. Idempotent and cheap: called whenever the capture status, the
    /// device list, the calibrations, the displays, or a calibration run changes.
    /// </summary>
    private void SyncCameraFallback()
    {
        // The camera Tracking was turned off for is back, or the user took over: the status is "Paused" again.
        if (LostCameraId is not null && (TrackingEnabled || IsChosenCameraConnected))
        {
            LostCameraId = null;
        }

        if (FallbackCamera is not null && IsChosenCameraConnected)
        {
            CancelCameraLossWait();

            // Never restart the source while the camera is suspended (asleep, locked): that waits for the wake. A switch
            // back that just failed waits for its retry.
            if (!CalibrationRunning && Status is not CaptureStatus.Suspended && !_fallbackSwitchBackDeferred)
            {
                EndCameraFallback();
            }

            return;
        }

        if (!TrackingEnabled || !(IsRunningCameraLost || IsRetryPending))
        {
            CancelCameraLossWait();
            return;
        }

        if (_cameraLossElapsed)
        {
            StartCameraFallback();
        }
        else if (_cameraLossTimer is null)
        {
            _cameraLossTimer = _scheduler.Schedule(CameraFallbackDelay, CameraLossWaitElapsed);
        }
    }

    private void CameraLossWaitElapsed()
    {
        _cameraLossTimer = null;
        _cameraLossElapsed = true;
        SyncCameraFallback();
    }

    private void CancelCameraLossWait()
    {
        _cameraLossTimer?.Cancel();
        _cameraLossTimer = null;
        _cameraLossElapsed = false;
    }

    // The wait is over: restart on a calibrated stand-in, if there is one now. Otherwise nothing is left to use, and
    // Tracking is turned off.
    private void StartCameraFallback()
    {
        if (!TrackingEnabled || CalibrationRunning || Status is CaptureStatus.Suspended)
        {
            return;
        }

        if (IsChosenCameraConnected)
        {
            // Only reached when a start failed and nothing runs: the chosen camera is back, so start on it.
            CancelCameraLossWait();
            RefreshActiveCalibration(reconcile: false);
            ReconcileForFallback(FallbackRestart.Chosen(null));
            return;
        }

        if (FallbackCandidate() is not { } device)
        {
            TurnTrackingOffForLostCamera();
            return;
        }

        CancelCameraLossWait();
        FallbackCamera = device;
        Log("The chosen camera is unplugged; continuing with another calibrated camera");

        // From here the calibration is looked up for the stand-in, and the changed configuration restarts the source.
        RefreshActiveCalibration(reconcile: false);
        ReconcileForFallback(FallbackRestart.StandIn);
    }

    // No connected camera has a calibration for these displays: turn Tracking off, in memory only (the saved choice is
    // the user's, as for a camera failure, D23), and let the camera go. The status names the chosen camera until it is
    // connected again, and Tracking stays off until the user turns it on.
    private void TurnTrackingOffForLostCamera()
    {
        if (SelectedCameraId is not { } id)
        {
            return;
        }

        Log("The chosen camera is unplugged and no other camera is calibrated; Tracking is turned off");
        ClearCameraFallback();
        LostCameraId = id;

        // A failed retry left "failed" on screen; the lost camera is the reason, and the status names it.
        if (Status is CaptureStatus.Failed)
        {
            Status = new CaptureStatus.Stopped();
        }

        TrackingEnabled = false;
        ResetTracking();
        SyncTrackingActivity();
        _ = ReconcileCamera();
    }

    // The chosen camera is connected again: restart on it.
    private void EndCameraFallback()
    {
        if (FallbackCamera is not { } standIn)
        {
            return;
        }

        FallbackCamera = null;
        Log("The chosen camera is connected again");
        RefreshActiveCalibration(reconcile: false);
        ReconcileForFallback(FallbackRestart.Chosen(standIn));
    }

    // Queues the one reconcile that carries out the restart, marked as the fallback's own so that a start failing because
    // the camera is not ready keeps Tracking on. The mark ends with that reconcile, whatever it did.
    private void ReconcileForFallback(FallbackRestart restart)
    {
        _fallbackRestart = restart;
        _fallbackRestartSerial++;
        var serial = _fallbackRestartSerial;
        _ = EnqueueCameraWork(async () =>
        {
            await PerformCameraReconcileAsync();
            if (_fallbackRestartSerial == serial)
            {
                _fallbackRestart = null;
            }
        });
    }

    /// <summary>
    /// A start that the fallback began failed. When the camera was merely not ready to open (DeviceUnavailable, NoDevice:
    /// a camera just plugged in), Tracking stays on and the fallback tries again later; returns true. Any other error
    /// returns false, and the caller handles it as before.
    /// </summary>
    private bool RecoverFromFailedFallbackStart()
    {
        if (_fallbackRestart is not { } restart)
        {
            return false;
        }

        _fallbackRestart = null;
        if (PermissionPrompt is not null || Status is not CaptureStatus.Failed { Error: var error } || !error.IsCameraNotReady())
        {
            return false;
        }

        Log("The camera was not ready to open; Tracking stays on and the fallback tries again");
        if (restart.OnChosen && restart.Replacing is { } standIn)
        {
            // Back on the stand-in, and not tried again at once: every start of the stand-in would trigger it.
            FallbackCamera = standIn;
            _fallbackSwitchBackDeferred = true;
            RefreshActiveCalibration(reconcile: false);
            ReconcileForFallback(FallbackRestart.StandIn);
        }
        else
        {
            // Nothing runs. The 5 s are over, so the next device change (or the retry) starts a camera again.
            FallbackCamera = null;
            _cameraLossElapsed = true;
        }

        ScheduleFallbackRetry();
        return true;
    }

    private void ScheduleFallbackRetry()
    {
        _fallbackRetryTimer?.Cancel();
        _fallbackRetryTimer = _scheduler.Schedule(CameraFallbackDelay, () =>
        {
            _fallbackRetryTimer = null;
            _fallbackSwitchBackDeferred = false;
            SyncCameraFallback();
        });
    }

    /// <summary>The device list changed: a switch back that failed may work now.</summary>
    private void DeviceListDidChange() => _fallbackSwitchBackDeferred = false;

    /// <summary>Forgets the stand-in and the wait: the camera stops, or the user chose a camera. Starts nothing.</summary>
    private void ClearCameraFallback()
    {
        CancelCameraLossWait();
        _fallbackRetryTimer?.Cancel();
        _fallbackRetryTimer = null;
        _fallbackSwitchBackDeferred = false;
        _fallbackRestart = null;
        FallbackCamera = null;
    }
}
