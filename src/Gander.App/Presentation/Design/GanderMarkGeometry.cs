using System;
using System.Collections.Generic;

namespace Gander.App.Presentation.Design;

/// <summary>A point, y down.</summary>
public readonly record struct MarkPoint(double X, double Y);

/// <summary>A rectangle, y down.</summary>
public readonly record struct MarkRect(double X, double Y, double Width, double Height)
{
    public double MidX => X + Width / 2;

    public double MidY => Y + Height / 2;

    public double Right => X + Width;

    public double Bottom => Y + Height;
}

/// <summary>What a <see cref="MarkStep"/> does.</summary>
public enum MarkStepKind
{
    /// <summary>Starts a figure at <see cref="MarkStep.A"/>.</summary>
    Move,

    /// <summary>A straight line to <see cref="MarkStep.A"/>.</summary>
    Line,

    /// <summary>A cubic curve with control points <see cref="MarkStep.A"/> and <see cref="MarkStep.B"/>, ending at <see cref="MarkStep.C"/>.</summary>
    Cubic,

    /// <summary>Closes the figure.</summary>
    Close,
}

/// <summary>One step of the mark's outline.</summary>
public readonly record struct MarkStep(MarkStepKind Kind, MarkPoint A = default, MarkPoint B = default, MarkPoint C = default);

/// <summary>
/// The Gander mark (Mac GanderMarkGeometry): a goose in profile, head up and looking right, with a wing. One definition in
/// goose coordinates with y down, shared by the tray glyph, the WPF mark and the app icon, so the mark looks the same at
/// every scale. A <see cref="Placement"/> maps it into a drawing area.
/// </summary>
public static class GanderMarkGeometry
{
    /// <summary>Side of the square the mark sits in when it keeps the app icon's padding.</summary>
    public const double UnitSize = 100;

    /// <summary>Share of the unit square left empty on each side when the mark keeps the app icon's padding.</summary>
    public const double SquarePadding = 0.16;

    /// <summary>The eye: a small round hole in the head.</summary>
    public static readonly MarkPoint EyeCenter = new(60.8, 17.1);

    public const double EyeRadius = 1.6;

    /// <summary>What the goose covers, in goose coordinates.</summary>
    public static readonly MarkRect VisualBounds = new(14, 10.9, 68.3, 75.8);

    private static MarkStep Move(double x, double y) => new(MarkStepKind.Move, new MarkPoint(x, y));

    private static MarkStep Line(double x, double y) => new(MarkStepKind.Line, new MarkPoint(x, y));

    private static MarkStep Cubic(double x1, double y1, double x2, double y2, double x, double y) =>
        new(MarkStepKind.Cubic, new MarkPoint(x1, y1), new MarkPoint(x2, y2), new MarkPoint(x, y));

    /// <summary>The outline of the body, neck, head and beak, without the eye.</summary>
    public static readonly IReadOnlyList<MarkStep> Silhouette =
    [
        Move(14, 70),
        Cubic(22, 72, 32, 66, 42, 64),
        Cubic(48, 63, 50, 58, 49, 50),
        Cubic(48, 40, 46, 34, 47, 26),
        Cubic(47.5, 18, 50, 12, 57, 11),
        Cubic(62, 10.5, 65, 13, 66, 15.5),
        Line(76.5, 18),
        Line(67, 22.5),
        Cubic(64, 23.5, 61.5, 24.5, 60.5, 28),
        Cubic(59.5, 34, 60.5, 40, 62, 46),
        Cubic(64, 52, 72, 54, 78, 60),
        Cubic(86, 68, 82, 80, 70, 84),
        Cubic(54, 90, 28, 86, 14, 70),
        new(MarkStepKind.Close),
    ];

    /// <summary>The wing: a leaf shape laid on the body.</summary>
    public static readonly IReadOnlyList<MarkStep> Wing =
    [
        Move(32, 73),
        Cubic(42, 65, 58, 65, 69, 71),
        Cubic(60, 80, 42, 80, 32, 73),
        new(MarkStepKind.Close),
    ];

    /// <summary>Where goose coordinates land in a drawing area: a uniform scale and an offset.</summary>
    public readonly record struct Placement(double Scale, MarkPoint Offset)
    {
        /// <summary>A point in goose coordinates, in the drawing area.</summary>
        public MarkPoint Point(MarkPoint unit) => new(Offset.X + unit.X * Scale, Offset.Y + unit.Y * Scale);

        /// <summary>A length in goose coordinates, in the drawing area.</summary>
        public double Length(double unit) => unit * Scale;
    }

    /// <summary>Scales and centers <see cref="VisualBounds"/> so the goose fills <paramref name="rect"/> as much as it can: for small marks.</summary>
    public static Placement Fitting(MarkRect rect)
    {
        var bounds = VisualBounds;
        var scale = Math.Min(rect.Width / bounds.Width, rect.Height / bounds.Height);
        return new Placement(scale, new MarkPoint(rect.MidX - bounds.MidX * scale, rect.MidY - bounds.MidY * scale));
    }

    /// <summary>The goose in the largest centered square in <paramref name="rect"/>, with the padding it has in the app icon.</summary>
    public static Placement UnitSquareIn(MarkRect rect)
    {
        var side = Math.Min(rect.Width, rect.Height);
        var inner = side * (1 - 2 * SquarePadding);
        return Fitting(new MarkRect(rect.MidX - inner / 2, rect.MidY - inner / 2, inner, inner));
    }
}

/// <summary>
/// The tray icon's drawing numbers (Mac MenuBarIcon). The design is 18 x 18 points; any pixel size is that times
/// <c>size / 18</c>. Weights are set by hand because a tray icon needs strokes a little heavier than the mark scaled down.
/// </summary>
public static class TrayGlyphSpec
{
    /// <summary>Side of the design in points.</summary>
    public const double Side = 18;

    /// <summary>The eye's radius: set by hand, because a hole that small must be larger than the mark scaled down.</summary>
    public const double EyeRadius = 0.55;

    /// <summary>Width of the outline that is the hollow (paused) goose: about 1.5 pixels at 16, so it stays visible.</summary>
    public const double OutlineWidth = 1.7;

    /// <summary>The area the mark is fitted into: a little left of and above the center, which leaves a corner for badges.</summary>
    public static readonly MarkRect MarkArea = new(0.75, 0.8, 15, 15);

    /// <summary>The lock badge's body: bottom right.</summary>
    public static readonly MarkRect LockBody = new(12.4, 12, 5.2, 4.4);

    public const double LockBodyRadius = 1.2;
    public const double ShackleStroke = 1.2;
    public const double BadgeGap = 1;
    public const double LockOutlineRadius = 2.2;

    /// <summary>The shackle: an arch above the body.</summary>
    public static MarkRect Shackle => new(LockBody.X + 1.1, LockBody.Y - 2.4, LockBody.Width - 2.2, 5.2);

    /// <summary>The clear area cut around the lock.</summary>
    public static MarkRect LockOutline
    {
        get
        {
            var shackle = Shackle;
            var x = Math.Min(LockBody.X, shackle.X);
            var y = Math.Min(LockBody.Y, shackle.Y);
            var right = Math.Max(LockBody.Right, shackle.Right);
            var bottom = Math.Max(LockBody.Bottom, shackle.Bottom);
            return new MarkRect(x - BadgeGap, y - BadgeGap, right - x + 2 * BadgeGap, bottom - y + 2 * BadgeGap);
        }
    }

    /// <summary>The attention dot: top right.</summary>
    public static readonly MarkPoint AlertCenter = new(15.5, 2.5);

    public const double AlertRadius = 2.0;

    /// <summary>The clear ring around the dot is this much wider than the dot.</summary>
    public const double AlertGap = 0.9;

    /// <summary>The mark's placement inside the 18 point design.</summary>
    public static GanderMarkGeometry.Placement Placement => GanderMarkGeometry.Fitting(MarkArea);
}
