using Gander.Core.Runtime;

namespace Gander.Core.Controller;

// The camera's grace after a display change (SPEC section 6, Mac CONTRACT 6.5.5 item 6). Unplugging a display, or a dock
// bringing its displays back one at a time, can leave no calibration that fits for a moment. Turning the camera off at
// once and on again a second later would blink its light, so the camera stays for up to 2 s and the stop is cancelled if
// the layout recovers. Only a display change gets this grace: Tracking off, Pause, quit, and lock stop the camera at once.
public sealed partial class TrackingController
{
    /// <summary>How long the camera stays on after a display change left no calibration that fits, in seconds.</summary>
    public const double CameraGraceDelay = 2;

    private IScheduledAction? _cameraGraceTimer;

    /// <summary>True while a stop is waiting out its grace.</summary>
    public bool IsCameraGraceActive => _cameraGraceTimer is not null;

    // A display change just took away the calibration the running camera was started for: stop it in CameraGraceDelay
    // seconds instead of at once, unless the layout recovers first.
    private void BeginCameraGrace()
    {
        if (!TrackingEnabled || !IsCameraActive || WantsCamera || _cameraGraceTimer is not null)
        {
            return;
        }

        _cameraGraceTimer = _scheduler.Schedule(CameraGraceDelay, CameraGraceExpired);
    }

    private void CancelCameraGrace()
    {
        _cameraGraceTimer?.Cancel();
        _cameraGraceTimer = null;
    }

    private void CameraGraceExpired()
    {
        _cameraGraceTimer = null;

        // The reconcile stops the camera unless a calibration fits again by now.
        _ = ReconcileCamera();
    }
}
