namespace Sway.Core.Controller;

// Calibration hand-off and shutdown.
public sealed partial class TrackingController
{
    /// <summary>
    /// A calibration run (a future UI) needs the camera and keeps the tracking loop out, whether Tracking is on or not
    /// (Mac: gate test and Setup camera check). Starts or stops the camera to match.
    /// </summary>
    public async Task SetCalibrationRunningAsync(bool running)
    {
        if (running == CalibrationRunning)
        {
            return;
        }

        CalibrationRunning = running;
        ResetTracking();
        SyncTrackingActivity();
        Publish();
        await ReconcileCamera();
        Publish();
    }

    /// <summary>Drops every pending one-shot delay: the holds, the device list refresh, the camera grace, and the camera wait.</summary>
    private void CancelPendingTimers()
    {
        CancelDisplayAndDesktopHolds();
        CancelCameraGrace();
        ClearCameraFallback();
        CancelSourceRetry();
        _environmentRefreshTimer?.Cancel();
        _environmentRefreshTimer = null;
    }

    /// <summary>
    /// Quitting: turns Tracking off in memory only (the saved choice is the user's, so the next launch restores it) and
    /// stops the camera, completing when the camera is off.
    /// </summary>
    public async Task ShutdownAsync()
    {
        CancelPendingTimers();
        TrackingEnabled = false;
        CalibrationRunning = false;
        ResetTracking();
        SyncTrackingActivity();
        await ReconcileCamera();
        Publish();
    }
}
