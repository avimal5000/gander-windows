namespace Sway.Core.Pose;

/// <summary>
/// Where to look for a face with the landmark model when the tracker has none and the system face detector found
/// nothing (a face turned far to one side is often missed by the detector). Produces candidate boxes in a fixed order
/// and hands out a few per frame, rotating through the list across frames so CPU stays low.
/// </summary>
public sealed class FaceSearchPlan
{
    /// <summary>Model attempts allowed per frame.</summary>
    public const int DefaultAttemptsPerFrame = 2;

    private const double FallbackSideFraction = 0.6; // of the shorter frame side, when no box is known
    private const double LargeScale = 1.5;
    private const double ShiftFraction = 0.5; // of the box side
    private const double WideHeightFraction = 0.7; // of the frame height

    private int _cursor;

    /// <summary>Starts over at the first candidate (call when a face is found).</summary>
    public void Reset() => _cursor = 0;

    /// <summary>
    /// The full ordered candidate list: (a) the last box center at two scales, (b) shifted left and right by half a box
    /// width, (c) a wide box over the central 70 percent of the frame height. All are clamped inside the frame.
    /// </summary>
    public static IReadOnlyList<SquareBox> Candidates(SquareBox? last, int frameWidth, int frameHeight)
    {
        if (frameWidth <= 0 || frameHeight <= 0)
        {
            return [];
        }

        var shortSide = Math.Min(frameWidth, frameHeight);
        var centerX = last?.CenterX ?? (frameWidth / 2.0);
        var centerY = last?.CenterY ?? (frameHeight / 2.0);
        var side = last is { Side: > 1 } known ? known.Side : shortSide * FallbackSideFraction;
        if (!double.IsFinite(centerX) || !double.IsFinite(centerY) || !double.IsFinite(side))
        {
            centerX = frameWidth / 2.0;
            centerY = frameHeight / 2.0;
            side = shortSide * FallbackSideFraction;
        }

        return
        [
            Clamp(centerX, centerY, side, frameWidth, frameHeight),
            Clamp(centerX, centerY, side * LargeScale, frameWidth, frameHeight),
            Clamp(centerX - (side * ShiftFraction), centerY, side, frameWidth, frameHeight),
            Clamp(centerX + (side * ShiftFraction), centerY, side, frameWidth, frameHeight),
            Clamp(frameWidth / 2.0, frameHeight / 2.0, frameHeight * WideHeightFraction, frameWidth, frameHeight),
        ];
    }

    /// <summary>
    /// The next <paramref name="count"/> candidates (at most one full lap) and advances the rotation, so the following
    /// frame gets the following boxes.
    /// </summary>
    public IReadOnlyList<SquareBox> Next(SquareBox? last, int frameWidth, int frameHeight, int count = DefaultAttemptsPerFrame)
    {
        var all = Candidates(last, frameWidth, frameHeight);
        if (all.Count == 0 || count <= 0)
        {
            return [];
        }

        var take = Math.Min(count, all.Count);
        var picked = new SquareBox[take];
        for (var i = 0; i < take; i++)
        {
            picked[i] = all[(_cursor + i) % all.Count];
        }

        _cursor = (_cursor + take) % all.Count;
        return picked;
    }

    /// <summary>A square no larger than the frame's shorter side, moved so it lies inside the frame.</summary>
    private static SquareBox Clamp(double centerX, double centerY, double side, int frameWidth, int frameHeight)
    {
        var fitted = Math.Min(side, Math.Min(frameWidth, frameHeight));
        var half = fitted / 2;
        return new SquareBox(
            Math.Clamp(centerX, half, frameWidth - half),
            Math.Clamp(centerY, half, frameHeight - half),
            fitted);
    }
}
