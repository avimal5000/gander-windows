using Sway.Core.Runtime;

namespace Sway.Core.Controller;

// Switching waits while the displays or the virtual desktops change (SPEC D44, Mac CONTRACT 6.5.5 item 1). While either
// holds, the decider is told it is locked, so it drops any dwell and no switch fires. The timing lives in the Core
// ChangeHolds; this file feeds it and reacts to its events (reset the loop, drop a stale poll, poll focus once).
public sealed partial class TrackingController
{
    /// <summary>
    /// The displays changed (the platform raised <see cref="Platform.IDisplayCatalog.LayoutChanged"/>, possibly several
    /// times for one change). Holds switching, resets the loop, then applies the layout the catalog reports now; the
    /// hold ends when a new layout is applied, or after 0.75 s, or at the 3 s cap.
    /// </summary>
    private void OnLayoutChanged() => Post(DisplayLayoutNotified);

    private void DisplayLayoutNotified()
    {
        _holds.DisplayParametersDidChange();
        LayoutDidChange(_env.Displays.Current());
    }

    /// <summary>
    /// The active virtual desktop changed (called by the shell or platform). What the system reports as the front
    /// window is still settling, so switching holds for 0.6 s and a focus poll already running is dropped.
    /// </summary>
    public void ActiveDesktopChanged()
    {
        _holds.ActiveSpaceDidChange();
        Publish();
    }

    /// <summary>Sway's tray menu or one of its windows is in front (or no longer): automatic switching holds while it is (SPEC D28).</summary>
    public void SetSwayInFront(bool inFront)
    {
        if (inFront == SwayInFront)
        {
            return;
        }

        SwayInFront = inFront;
        if (inFront)
        {
            CancelPendingSwitch();
        }

        Publish();
    }

    /// <summary>Drops both holds and their timers (quitting).</summary>
    private void CancelDisplayAndDesktopHolds() => _holds.CancelAll();

    private void LayoutDidChange(DisplayLayout newLayout)
    {
        if (newLayout.Equals(Layout))
        {
            return;
        }

        var cameraWasWanted = WantsCamera;
        HoldReconcile();
        Layout = newLayout;
        _displayLabels = DisplayLabeler.Labels(newLayout);

        // Display identities may have changed, so a dwell or the believed focus no longer means anything. A changed
        // calibration rebuilds the decider; an unchanged one still needs its targets and a clean loop.
        if (!RefreshActiveCalibration())
        {
            UpdateTrackedTargets();
            ResetTracking();
            if (_isPollingFocus)
            {
                PollFocusedDisplay();
            }
        }

        // A camera left without a calibration by this change stays up for a short grace, in case the layout recovers.
        if (cameraWasWanted)
        {
            BeginCameraGrace();
        }

        SyncCameraFallback();
        FlushReconcile();

        // The new layout is known: the hold that waited for it ends (SPEC D44).
        _holds.EndDisplaysChanging(HoldEnd.Layout);
    }
}
