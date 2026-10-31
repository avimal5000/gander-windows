using Gander.Core.Platform;

namespace Gander.Core.Tests.Controller;

/// <summary>Sample displays, cameras, poses, profiles and decisions shared by the controller tests (Mac Fixtures).</summary>
public static class Fixtures
{
    public static readonly DisplayInfo Left = new("display-left", 11, "Left", new DisplayRect(0, 0, 1920, 1080), true, true);
    public static readonly DisplayInfo Right = new("display-right", 12, "Right", new DisplayRect(1920, 0, 2560, 1440), false, false);
    public static readonly DisplayInfo Far = new("display-far", 13, "Far", new DisplayRect(4480, 0, 1920, 1080), false, false);
    public static readonly DisplayInfo Extra = new("display-extra", 14, "Extra", new DisplayRect(6400, 0, 1920, 1080), false, false);

    public static readonly DisplayLayout TwoDisplays = new([Left, Right]);
    public static readonly DisplayLayout ThreeDisplays = new([Left, Right, Far]);
    public static readonly DisplayLayout FourDisplays = new([Left, Right, Far, Extra]);
    public static readonly DisplayLayout OneDisplay = new([Left]);

    public static readonly CameraDevice BuiltInCamera = new("cam-builtin", "Integrated Camera", true);
    public static readonly CameraDevice ExternalCamera = new("cam-external", "Logitech C922", false);
    public static readonly CameraDevice SecondExternalCamera = new("cam-other", "Other USB", false);

    public static DisplayId LeftId => Left.Id;

    public static DisplayId RightId => Right.Id;

    public static DisplayId FarId => Far.Id;

    public static HeadPose Pose(double yaw = 0, double pitch = 0) => new(yaw, pitch, 0, 0.95);

    public static TimedPose Sample(double time, bool face = true, double yaw = 0, double pitch = 0) =>
        new(time, face ? Pose(yaw, pitch) : null);

    /// <summary>A Vision-angles profile: the first display at -20 yaw, then +20, +60 and so on.</summary>
    public static CalibrationProfile Profile(DisplayLayout? layout = null, string? cameraName = "Integrated Camera", long createdAt = 1_700_000_000)
    {
        layout ??= TwoDisplays;
        var displays = layout.Displays
            .Select((d, index) => new DisplayProfile(d.Id, -20 + (40.0 * index), 0, 2, 1, 100))
            .ToList();
        return new CalibrationProfile(
            PoseFeatureSet.VisionAngles, displays, 2, 2.5, layout.Fingerprint, cameraName, DateTimeOffset.FromUnixTimeSeconds(createdAt));
    }

    public static PoseClassification Classification(
        PoseClassification.ClassKind kind = PoseClassification.ClassKind.Display, DisplayId? best = null, double margin = 0.6)
    {
        if (kind == PoseClassification.ClassKind.NoFace)
        {
            return PoseClassification.NoFace;
        }

        var shown = best ?? Left.Id;
        var runnerUp = shown == Left.Id ? Right.Id : Left.Id;
        return new PoseClassification(
            kind, shown, runnerUp, kind == PoseClassification.ClassKind.Display ? margin : 0, new Dictionary<DisplayId, double>());
    }

    public static SwitchDecision Decision(
        DisplayId? switchTo = null, SwitchPhase? phase = null, SwitchHoldReason? reason = null, PoseClassification? classification = null) =>
        new(switchTo, phase ?? SwitchPhase.Idle, reason, classification ?? Classification());

    public static WindowSummary Window(long handle = 1, string app = "Notepad", DisplayInfo? on = null)
    {
        var display = on ?? Left;
        return new WindowSummary(handle, (int)handle + 100, app, display.Frame, display.Id);
    }
}
