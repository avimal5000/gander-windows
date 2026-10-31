namespace Sway.Core.Pose;

/// <summary>
/// Policy for keeping a face that is turning away from the camera. A face at 40 to 50 degrees of yaw is partly hidden,
/// so the landmark model's face-presence score drops and a strict 0.5 threshold drops the face even though the box is
/// still on it. Two rules help: a box that follows a face seen a moment ago is accepted at a lower score (hysteresis)
/// when the landmarks it produced still look like the same face, and after a loss the same neighbourhood is searched
/// first with growing boxes, leaning toward the way the face was moving.
/// </summary>
public static class FaceRecovery
{
    /// <summary>The score a face found from nothing (detector or blind search) must reach.</summary>
    public const double ColdThreshold = 0.5;

    /// <summary>The score a face must reach when the box follows a face seen within the last frames.</summary>
    public const double TrackedThreshold = 0.25;

    /// <summary>How many frames after a loss the neighbourhood search keeps trying (the face is probably still near).</summary>
    public const int NeighbourhoodFrames = 12;

    /// <summary>Boxes tried per frame by the neighbourhood search.</summary>
    public const int NeighbourhoodAttemptsPerFrame = 3;

    private const double MinSizeRatio = 0.5;
    private const double MaxSizeRatio = 2.0;
    private const double MaxCenterShiftInSides = 1.0;
    private const double GrowthPerFrame = 0.12;
    private const double MaxGrowth = 2.0;

    /// <summary>The score needed: <see cref="TrackedThreshold"/> when the box follows a known face, else <see cref="ColdThreshold"/>.</summary>
    public static double Threshold(bool followsKnownFace) => followsKnownFace ? TrackedThreshold : ColdThreshold;

    /// <summary>
    /// Whether a new tracking box is believable as the same face as <paramref name="previous"/>: not much bigger or
    /// smaller, and not far away. Guards the lower threshold against a hallucinated fit on the background.
    /// </summary>
    public static bool Plausible(SquareBox next, SquareBox previous)
    {
        if (previous.Side <= 1 || next.Side <= 1)
        {
            return false;
        }

        var ratio = next.Side / previous.Side;
        var shift = Math.Sqrt(Math.Pow(next.CenterX - previous.CenterX, 2) + Math.Pow(next.CenterY - previous.CenterY, 2));
        return ratio is >= MinSizeRatio and <= MaxSizeRatio && shift <= previous.Side * MaxCenterShiftInSides;
    }

    /// <summary>
    /// Boxes to try <paramref name="framesLost"/> frames after the face was lost (1 = the first frame). The last tracking
    /// box grows by 12 percent per lost frame up to twice its size, then shifted copies lean toward
    /// <paramref name="drift"/> (the center's per-frame movement before the loss, in pixels, null when unknown) or, with no
    /// drift, toward the nearer side of the frame. Order: grown box, shifted toward the drift, shifted the other way. All are
    /// clamped inside the frame. Empty after <see cref="NeighbourhoodFrames"/> frames or when there is no last box.
    /// </summary>
    public static IReadOnlyList<SquareBox> Neighbourhood(SquareBox? last, (double X, double Y)? drift, int framesLost, int frameWidth, int frameHeight)
    {
        if (last is not { } box || framesLost < 1 || framesLost > NeighbourhoodFrames || frameWidth <= 0 || frameHeight <= 0 || !double.IsFinite(box.Side) || box.Side <= 1)
        {
            return [];
        }

        var growth = Math.Min(1 + (GrowthPerFrame * framesLost), MaxGrowth);
        var side = box.Side * growth;
        var leanRight = drift is { } d && Math.Abs(d.X) > 0.5 ? d.X > 0 : box.CenterX > frameWidth / 2.0;
        var lean = leanRight ? 1 : -1;
        return
        [
            Fit(box.CenterX, box.CenterY, side, frameWidth, frameHeight),
            Fit(box.CenterX + (lean * side * 0.35), box.CenterY, side, frameWidth, frameHeight),
            Fit(box.CenterX - (lean * side * 0.35), box.CenterY, side, frameWidth, frameHeight),
        ];
    }

    /// <summary>The next boxes from the neighbourhood list, rotating by <paramref name="framesLost"/> so each frame tries a different slice.</summary>
    public static IReadOnlyList<SquareBox> NeighbourhoodForFrame(SquareBox? last, (double X, double Y)? drift, int framesLost, int frameWidth, int frameHeight, int count = NeighbourhoodAttemptsPerFrame) =>
        Neighbourhood(last, drift, framesLost, frameWidth, frameHeight).Take(Math.Max(count, 0)).ToList();

    private static SquareBox Fit(double centerX, double centerY, double side, int frameWidth, int frameHeight)
    {
        var fitted = Math.Min(side, Math.Min(frameWidth, frameHeight));
        var half = fitted / 2;
        return new SquareBox(Math.Clamp(centerX, half, frameWidth - half), Math.Clamp(centerY, half, frameHeight - half), fitted);
    }
}
