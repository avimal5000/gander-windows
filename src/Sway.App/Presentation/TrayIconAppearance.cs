using System;
using Sway.Core.Controller;

namespace Sway.App.Presentation;

/// <summary>How the tray mark is drawn for one <see cref="TrayIconState"/> (pure: no drawing here).</summary>
/// <param name="FilledEnd">The larger end point is solid (tracking) rather than a hollow ring (paused).</param>
/// <param name="Dimmed">The whole mark is drawn at reduced strength (paused).</param>
/// <param name="LockBadge">A small padlock in the corner (Focus Lock).</param>
/// <param name="AttentionDot">A dot in the corner (Needs you).</param>
public readonly record struct TrayIconAppearance(bool FilledEnd, bool Dimmed, bool LockBadge, bool AttentionDot)
{
    /// <summary>Sizes drawn at runtime, for 100%, 125%, 150% and 200% scaling.</summary>
    public static readonly int[] Sizes = [16, 20, 24, 32];

    /// <summary>The look for a state: solid when tracking, hollow and dimmed when paused, padlock for Focus Lock, dot for Needs you.</summary>
    public static TrayIconAppearance For(TrayIconState state) => state switch
    {
        TrayIconState.Tracking => new TrayIconAppearance(true, false, false, false),
        TrayIconState.Paused => new TrayIconAppearance(false, true, false, false),
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

    /// <summary>The tooltip: "Sway: &lt;status&gt;", cut to the 63 characters a notify icon accepts.</summary>
    public static string Tooltip(string statusLine)
    {
        const int limit = 63;
        var text = $"Sway: {statusLine}";
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

/// <summary>The icon colors for a taskbar theme: ivory, forest green and sage (plus an amber for Needs you).</summary>
public sealed record TrayPalette(Rgb Mark, Rgb Accent, Rgb Attention)
{
    /// <summary>Forest green #2F5D3A.</summary>
    public static readonly Rgb Forest = Rgb.Hex("#2F5D3A");

    /// <summary>Sage #9CAF88.</summary>
    public static readonly Rgb Sage = Rgb.Hex("#9CAF88");

    /// <summary>Ivory #F5F1E6.</summary>
    public static readonly Rgb Ivory = Rgb.Hex("#F5F1E6");

    /// <summary>Amber, the one color outside the brand palette, so the Needs-you dot reads on both themes.</summary>
    public static readonly Rgb Amber = Rgb.Hex("#E0902B");

    /// <summary>Forest marks on a light taskbar, ivory marks on a dark one; sage is the small dot either way.</summary>
    public static TrayPalette For(bool lightTaskbar) => new(lightTaskbar ? Forest : Ivory, Sage, Amber);

    /// <summary>
    /// Reads the taskbar theme from the registry value SystemUsesLightTheme (0 means dark). A missing value counts as
    /// light, which is what Windows does before the setting exists.
    /// </summary>
    public static bool IsLight(object? systemUsesLightTheme) => systemUsesLightTheme is not int value || value != 0;
}
