using System.Collections.Generic;
using System.Linq;

namespace Sway.App.Presentation.Design;

/// <summary>Which display edge a piece of the glow sits on.</summary>
public enum GlowEdge
{
    Left,
    Right,
    Bottom,
    Top,
}

/// <summary>One solid border strip, one soft glow strip and the direction the glow fades, for an edge.</summary>
public sealed record GlowStrip(GlowEdge Edge, MarkRect Border, MarkRect Glow, MarkPoint GradientStart, MarkPoint GradientEnd);

/// <summary>
/// The edge glow's geometry (Mac EdgeGlowView, SPEC F27): a 4 pt border along the edges with a glow of about 10 pt just
/// inside it, fading out from 40% opacity. Eight thin strips, so there is no full-screen bitmap and the middle of the
/// display has nothing drawn. Pure; y down, units are device independent pixels.
/// </summary>
public static class EdgeGlowLayout
{
    /// <summary>The four strips for a display of the given size. Side strips run the full height; top and bottom run between them.</summary>
    public static IReadOnlyList<GlowStrip> Strips(double width, double height)
    {
        var b = SwayDesign.Glow.BorderWidth;
        var g = SwayDesign.Glow.GlowWidth;
        var innerWidth = System.Math.Max(width - 2 * b, 0);
        var innerHeight = System.Math.Max(height - 2 * b, 0);
        return
        [
            new(GlowEdge.Left, new MarkRect(0, 0, b, height), new MarkRect(b, b, g, innerHeight), new MarkPoint(0, 0.5), new MarkPoint(1, 0.5)),
            new(GlowEdge.Right, new MarkRect(width - b, 0, b, height), new MarkRect(width - b - g, b, g, innerHeight), new MarkPoint(1, 0.5), new MarkPoint(0, 0.5)),
            new(GlowEdge.Bottom, new MarkRect(b, height - b, innerWidth, b), new MarkRect(b, height - b - g, innerWidth, g), new MarkPoint(0.5, 1), new MarkPoint(0.5, 0)),
            new(GlowEdge.Top, new MarkRect(b, 0, innerWidth, b), new MarkRect(b, b, innerWidth, g), new MarkPoint(0.5, 0), new MarkPoint(0.5, 1)),
        ];
    }

    /// <summary>
    /// The opacity keyframes of a feedback: (seconds, opacity) pairs. Animated: in, hold, out. Reduced motion: fully visible
    /// for the plain duration, then gone, with nothing fading.
    /// </summary>
    public static IReadOnlyList<(double Seconds, double Opacity)> Keyframes(FeedbackTiming timing, bool animated) => animated
        ? [(0, 0), (timing.FadeIn, 1), (timing.FadeIn + timing.Hold, 1), (timing.TotalDuration(true), 0)]
        : [(0, 1), (timing.PlainDuration, 1)];

    /// <summary>The seconds after which the window is hidden.</summary>
    public static double HideAfter(FeedbackTiming timing, bool animated) => Keyframes(timing, animated).Last().Seconds;
}
