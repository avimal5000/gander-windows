namespace Gander.Core.Calibration;

/// <summary>
/// The live hint on the Setup window's camera check (SPEC F20): whether the camera sees the face well enough to
/// calibrate. A pure function of the latest pose. Port of the Mac <c>FramingAdvice</c>.
/// </summary>
public enum FramingAdvice
{
    /// <summary>No face (or no face box) in the frame.</summary>
    NoFace,

    /// <summary>The face is too small: move closer.</summary>
    Closer,

    /// <summary>The face is too big: move back.</summary>
    Back,

    /// <summary>The face is too far from the middle of the frame.</summary>
    Center,

    /// <summary>Well framed.</summary>
    Good,
}

/// <summary>Rules and wording for <see cref="FramingAdvice"/>.</summary>
public static class FramingAdvices
{
    /// <summary>A face box shorter than this (fraction of the frame height) is too far away.</summary>
    public const double MinimumFaceHeight = 0.18;

    /// <summary>A face box taller than this is too close.</summary>
    public const double MaximumFaceHeight = 0.6;

    /// <summary>Lower end of the range the face center must stay inside, on both axes (fraction of the frame).</summary>
    public const double CenterRangeLow = 0.2;

    /// <summary>Upper end of the center range.</summary>
    public const double CenterRangeHigh = 0.8;

    /// <summary>The advice for <paramref name="pose"/>; the first rule that applies wins, in the order of the enum.</summary>
    public static FramingAdvice For(HeadPose? pose)
    {
        if (pose?.FaceBounds is not { } bounds)
        {
            return FramingAdvice.NoFace;
        }

        if (bounds.Height < MinimumFaceHeight)
        {
            return FramingAdvice.Closer;
        }

        if (bounds.Height > MaximumFaceHeight)
        {
            return FramingAdvice.Back;
        }

        return InCenterRange(bounds.MidX) && InCenterRange(bounds.MidY) ? FramingAdvice.Good : FramingAdvice.Center;
    }

    /// <summary>The words shown for <paramref name="advice"/>.</summary>
    public static string Text(this FramingAdvice advice) => advice switch
    {
        FramingAdvice.NoFace => "Gander can't see your face yet.",
        FramingAdvice.Closer => "Move a little closer to the camera.",
        FramingAdvice.Back => "Move back a little.",
        FramingAdvice.Center => "Center your face in the camera's view.",
        _ => "Looks good.",
    };

    private static bool InCenterRange(double value) => value >= CenterRangeLow && value <= CenterRangeHigh;
}
