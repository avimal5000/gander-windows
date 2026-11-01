using System;
using Gander.Core.Controller;

namespace Gander.App.Presentation;

/// <summary>How the tray mark is drawn for one <see cref="TrayIconState"/> (pure: no drawing here).</summary>
/// <param name="FilledEnd">The larger end point is solid (tracking) rather than a hollow ring (paused).</param>
/// <param name="Dimmed">The whole mark is drawn at reduced strength (paused).</param>
/// <param name="LockBadge">A small padlock in the corner (Focus Lock).</param>
/// <param name="AttentionDot">A dot at the top right (Needs you).</param>
public readonly record struct TrayIconAppearance(bool FilledEnd, bool Dimmed, bool LockBadge, bool AttentionDot)
{
    /// <summary>1 for a solid mark; 0.5 for the dimmed one (Mac MenuBarGlyph.Style.opacity).</summary>
    public double Opacity => Dimmed ? 0.5 : 1;

    /// <summary>The end point is a ring instead of a disc.</summary>
    public bool HollowEnd => !FilledEnd;

    /// <summary>Sizes drawn at runtime, for 100%, 125%, 150% and 200% scaling.</summary>
    public static readonly int[] Sizes = [16, 20, 24, 32];

    /// <summary>
    /// The look for a state: solid when tracking, an outline when paused, padlock for Focus Lock, dot for Needs you.
    /// The Mac also dims the paused glyph; Windows does not, because a one-pixel outline at half strength all but
    /// disappears on a dark taskbar at 16 pixels, and paused is what a new user sees until they calibrate.
    /// </summary>
    public static TrayIconAppearance For(TrayIconState state) => state switch
    {
        TrayIconState.Tracking => new TrayIconAppearance(true, false, false, false),
        TrayIconState.Paused => new TrayIconAppearance(false, false, false, false),
        TrayIconState.FocusLock => new TrayIconAppearance(true, false, true, false),
        _ => new TrayIconAppearance(true, false, false, true),
    };

    /// <summary>The drawn size nearest to what the shell asks for (the small icon size, in pixels).</summary>
    public static int NearestSize(int requestedPixels)
    {
        var best = Sizes[0];
        foreach (var size in Sizes)
        {
            if (Math.Abs(size - requestedPixels) < Math.Abs(best - requestedPixels))
            {
                best = size;
            }
        }

        return best;
    }

    /// <summary>The tooltip: "Gander: &lt;status&gt;", cut to the 63 characters a notify icon accepts.</summary>
    public static string Tooltip(string statusLine)
    {
        const int limit = 63;
        var text = $"Gander: {statusLine}";
        return text.Length <= limit ? text : text[..(limit - 1)] + "…";
    }
}

/// <summary>An ARGB color as four bytes, so the palette can be checked without a drawing library.</summary>
public readonly record struct Rgb(byte R, byte G, byte B)
{
    /// <summary>Parses "#RRGGBB".</summary>
    public static Rgb Hex(string text) =>
        new(Convert.ToByte(text.Substring(1, 2), 16), Convert.ToByte(text.Substring(3, 2), 16), Convert.ToByte(text.Substring(5, 2), 16));
}

/// <summary>
/// The tray icon is a template image on the Mac: one flat color that the bar tints for light and dark. Windows has no
/// template icons, so the mark is drawn in the text color of the taskbar theme (charcoal on light, ivory on dark).
/// </summary>
public sealed record TrayPalette(Rgb Mark)
{
    /// <summary>Forest green #2F5D3A (from the shared design tokens).</summary>
    public static readonly Rgb Forest = Design.GanderDesign.Forest;

    /// <summary>Sage #9CAF88.</summary>
    public static readonly Rgb Sage = Design.GanderDesign.Sage;

    /// <summary>Ivory #F6F3EA.</summary>
    public static readonly Rgb Ivory = Design.GanderDesign.Ivory;

    /// <summary>Charcoal marks on a light taskbar, ivory marks on a dark one.</summary>
    public static TrayPalette For(bool lightTaskbar) => new(lightTaskbar ? Design.GanderDesign.Charcoal : Design.GanderDesign.Ivory);

    /// <summary>
    /// Reads the taskbar theme from the registry value SystemUsesLightTheme (0 means dark). A missing value counts as
    /// light, which is what Windows does before the setting exists.
    /// </summary>
    public static bool IsLight(object? systemUsesLightTheme) => systemUsesLightTheme is not int value || value != 0;
}
