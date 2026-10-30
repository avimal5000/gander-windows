using Sway.Core.Runtime;

namespace Sway.Core.Controller;

// How much frame rate the pose source is asked for (SPEC D45, Mac CONTRACT 6.5.5 item 2). The source throttles itself;
// the controller only says what the app needs, and only when that changes. The decision is the Core PoseDemandPolicy.
public sealed partial class TrackingController
{
    private PoseDemand? _appliedPoseDemand;
    private bool _poseDebugVisible;
    private bool _focusTestVisible;

    /// <summary>What the camera is running for right now.</summary>
    public PoseDemand WantedPoseDemand => PoseDemandPolicy.Wanted(new PoseDemandInputs(
        GateTestRunning: false,
        SetupUsingCamera: CalibrationRunning,
        PoseDebugVisible: _poseDebugVisible,
        FocusTestVisible: _focusTestVisible,
        FocusLocked: FocusLocked,
        SwitchingUnavailable: false));

    /// <summary>
    /// Hands the wanted demand to the pose source when it differs from the one it has. Does nothing while no source
    /// runs: a start resets the source to Full, so a start applies the demand once.
    /// </summary>
    private void SyncPoseDemand(bool force = false)
    {
        if (_stream is null)
        {
            _appliedPoseDemand = null;
            return;
        }

        var demand = WantedPoseDemand;
        if (!force && demand == _appliedPoseDemand)
        {
            return;
        }

        _appliedPoseDemand = demand;
        _env.PoseSource.SetDemand(demand);
    }

    /// <summary>The Pose Debug window shows live content: it needs the full frame rate exactly then.</summary>
    public void SetPoseDebugVisible(bool visible)
    {
        if (visible == _poseDebugVisible)
        {
            return;
        }

        _poseDebugVisible = visible;
        SyncPoseDemand();
    }

    /// <summary>The Focus Test window is open: it needs the full frame rate.</summary>
    public void SetFocusTestVisible(bool visible)
    {
        if (visible == _focusTestVisible)
        {
            return;
        }

        _focusTestVisible = visible;
        SyncPoseDemand();
    }
}
