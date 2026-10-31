using System;

namespace Sway.App.Presentation.Design;

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

/// <summary>
/// The Swing mark (Mac SwayMarkGeometry, D36): an arc swinging from a small point on the left to a larger point on the
/// right. One definition in a 100 x 100 unit square with y down. The arc is the quadratic curve M 24 40 Q 50 86 76 40.
/// </summary>
public static class SwayMarkGeometry
{
    public const double UnitSize = 100;
    public static readonly MarkPoint Start = new(24, 40);
    public static readonly MarkPoint End = new(76, 40);

    /// <summary>The quadratic curve's control point.</summary>
    public static readonly MarkPoint Control = new(50, 86);

    public const double ArcWidth = 7;
    public const double StartRadius = 5.5;
    public const double EndRadius = 8;
    public const double RingWidth = 4;

    /// <summary>The two control points of the cubic curve equal to the arc (GDI+ and WPF have cubic curves).</summary>
    public static (MarkPoint First, MarkPoint Second) CubicControls() =>
    (
        new MarkPoint(Start.X + 2.0 / 3 * (Control.X - Start.X), Start.Y + 2.0 / 3 * (Control.Y - Start.Y)),
        new MarkPoint(End.X + 2.0 / 3 * (Control.X - End.X), End.Y + 2.0 / 3 * (Control.Y - End.Y)));

    /// <summary>The point of the arc at <paramref name="t"/> (0 at the start point, 1 at the end point).</summary>
    public static MarkPoint ArcPoint(double t)
    {
        var u = 1 - t;
        return new MarkPoint(
            u * u * Start.X + 2 * u * t * Control.X + t * t * End.X,
            u * u * Start.Y + 2 * u * t * Control.Y + t * t * End.Y);
    }

    /// <summary>What the mark covers, points and stroke included, in unit coordinates.</summary>
    public static MarkRect VisualBounds
    {
        get
        {
            var bottom = ArcPoint(0.5).Y + ArcWidth / 2;
            var left = Start.X - StartRadius;
            var right = End.X + EndRadius;
            var top = Math.Min(Start.Y - StartRadius, End.Y - EndRadius);
            return new MarkRect(left, top, right - left, bottom - top);
        }
    }

    /// <summary>Where unit coordinates land in a drawing area: a uniform scale and an offset.</summary>
    public readonly record struct Placement(double Scale, MarkPoint Offset)
    {
        /// <summary>A point in unit coordinates, in the drawing area.</summary>
        public MarkPoint Point(MarkPoint unit) => new(Offset.X + unit.X * Scale, Offset.Y + unit.Y * Scale);

        /// <summary>A length in unit coordinates, in the drawing area.</summary>
        public double Length(double unit) => unit * Scale;
    }

    /// <summary>Scales and centers <see cref="VisualBounds"/> so the mark fills <paramref name="rect"/>: for small marks.</summary>
    public static Placement Fitting(MarkRect rect)
    {
        var bounds = VisualBounds;
        var scale = Math.Min(rect.Width / bounds.Width, rect.Height / bounds.Height);
        return new Placement(scale, new MarkPoint(rect.MidX - bounds.MidX * scale, rect.MidY - bounds.MidY * scale));
    }

    /// <summary>Maps the whole unit square onto the largest centered square in <paramref name="rect"/>: the mark with the app icon's padding.</summary>
    public static Placement UnitSquareIn(MarkRect rect)
    {
        var side = Math.Min(rect.Width, rect.Height);
        return new Placement(side / UnitSize, new MarkPoint(rect.MidX - side / 2, rect.MidY - side / 2));
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

    public const double ArcWidth = 1.8;
    public const double StartRadius = 1.4;
    public const double EndRadius = 2;

    /// <summary>Width of the ring that is the hollow end point.</summary>
    public const double RingWidth = 1;

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
    public static SwayMarkGeometry.Placement Placement => SwayMarkGeometry.Fitting(MarkArea);
}
