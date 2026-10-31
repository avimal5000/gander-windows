using System;
using System.Collections.Generic;
using System.Linq;

namespace Sway.App.Presentation.Design;

/// <summary>An sRGB color with an opacity. Pure, so the palette can be tested without a drawing library.</summary>
public readonly record struct Rgba(byte R, byte G, byte B, byte A = 255)
{
    /// <summary>The color of <paramref name="rgb"/> at <paramref name="opacity"/> (0 to 1).</summary>
    public static Rgba Of(Rgb rgb, double opacity = 1) =>
        new(rgb.R, rgb.G, rgb.B, (byte)Math.Round(Math.Clamp(opacity, 0, 1) * 255));

    /// <summary>The opaque part.</summary>
    public Rgb Opaque => new(R, G, B);

    /// <summary>This color laid over <paramref name="background"/>: what a translucent token looks like on a surface.</summary>
    public Rgb Over(Rgb background)
    {
        var a = A / 255.0;
        return new(Mix(R, background.R, a), Mix(G, background.G, a), Mix(B, background.B, a));
    }

    private static byte Mix(byte top, byte bottom, double a) => (byte)Math.Round(top * a + bottom * (1 - a));
}

/// <summary>A font choice: the first family Windows has, then the fallback (WPF reads the comma list in order).</summary>
public sealed record FontStack(string Primary, string Fallback)
{
    /// <summary>The WPF family string: "Segoe UI Variable Text, Segoe UI".</summary>
    public string Wpf => $"{Primary}, {Fallback}";

    /// <summary>The first name of the stack that <paramref name="isInstalled"/> accepts (for GDI+, which has no list).</summary>
    public string Resolve(Func<string, bool> isInstalled) =>
        isInstalled(Primary) ? Primary : Fallback;
}

/// <summary>One step of the type scale.</summary>
/// <param name="Size">Device independent pixels.</param>
/// <param name="Weight">OpenType weight: 400 regular, 500 medium, 600 semibold.</param>
/// <param name="Display">Uses the display optical size of the font (large text).</param>
public sealed record TextStyle(double Size, int Weight, bool Display = false);

/// <summary>
/// The colors for one appearance. Light and dark are two instances of the same shape, so no window picks a color by
/// itself.
/// </summary>
public sealed record SwayTheme(
    bool IsLight,
    Rgb Window,
    Rgba Card,
    Rgba Line,
    Rgb Text,
    Rgba TextSecondary,
    Rgba Hover,
    Rgb Green,
    Rgb Accent,
    Rgb OnAccent,
    Rgb MenuSurface)
{
    /// <summary>The Mac's colors for a light or dark appearance. Accent is Sway green unless the user chose the system one.</summary>
    public static SwayTheme For(bool light, Rgb? systemAccent = null)
    {
        var green = SwayDesign.Green(light);
        var accent = systemAccent ?? SwayDesign.TextAccent(light);
        return light
            ? new SwayTheme(
                true,
                SwayDesign.WindowLight,
                Rgba.Of(SwayDesign.Ivory),
                new Rgba(0, 0, 0, 26),
                SwayDesign.Charcoal,
                new Rgba(0, 0, 0, 128),
                new Rgba(0, 0, 0, 20),
                green,
                accent,
                new Rgb(0xFF, 0xFF, 0xFF),
                SwayDesign.Ivory)
            : new SwayTheme(
                false,
                SwayDesign.WindowDark,
                Rgba.Of(new Rgb(0xFF, 0xFF, 0xFF), 0.06),
                new Rgba(255, 255, 255, 26),
                SwayDesign.Ivory,
                new Rgba(255, 255, 255, 140),
                new Rgba(255, 255, 255, 20),
                green,
                accent,
                SwayDesign.Charcoal,
                SwayDesign.Charcoal);
    }

    /// <summary>The menu surface, or any translucent token, as one opaque color (the tray menu cannot draw alpha).</summary>
    public Rgb Flat(Rgba token) => token.Over(MenuSurface);
}

/// <summary>What choosing the accent in Settings does (Mac AccentChoice).</summary>
public enum AccentChoice
{
    /// <summary>Sway green (the default).</summary>
    Sway,

    /// <summary>Follow the Windows accent color.</summary>
    System,
}

/// <summary>The accent choice's words and storage.</summary>
public static class AccentChoices
{
    /// <summary>Settings key, the Mac's.</summary>
    public const string Key = "sway.accentChoice";

    /// <summary>The label of a choice in Settings.</summary>
    public static string Title(AccentChoice choice) => choice == AccentChoice.System ? "Match Windows" : "Sway green";

    /// <summary>The saved choice; Sway green when absent or unreadable.</summary>
    public static AccentChoice Parse(string? stored) =>
        string.Equals(stored, "system", StringComparison.Ordinal) ? AccentChoice.System : AccentChoice.Sway;

    /// <summary>The text that is saved.</summary>
    public static string Store(AccentChoice choice) => choice == AccentChoice.System ? "system" : "sway";
}

/// <summary>How long one piece of feedback takes (Mac FeedbackTiming).</summary>
public sealed record FeedbackTiming(double FadeIn, double Hold, double FadeOut, double PlainDuration)
{
    /// <summary>Seconds from appearing to being gone.</summary>
    public double TotalDuration(bool animated) => animated ? FadeIn + Hold + FadeOut : PlainDuration;
}

/// <summary>
/// The single source of Sway's look on Windows: the Mac palette, the spacing and radius scale, the motion numbers and the
/// font choice. Every window, the tray menu and the overlays read from here. Pure data, no WPF types.
/// </summary>
public static class SwayDesign
{
    // ----- Palette (Mac SwayPalette, exact values) -----

    /// <summary>Forest #2F5D3A.</summary>
    public static readonly Rgb Forest = new(0x2F, 0x5D, 0x3A);

    /// <summary>Sage #9CAF88.</summary>
    public static readonly Rgb Sage = new(0x9C, 0xAF, 0x88);

    /// <summary>Ivory #F6F3EA.</summary>
    public static readonly Rgb Ivory = new(0xF6, 0xF3, 0xEA);

    /// <summary>Charcoal #2B2B2B.</summary>
    public static readonly Rgb Charcoal = new(0x2B, 0x2B, 0x2B);

    /// <summary>The lighter green that keeps Sway green readable on dark: #6FAF84.</summary>
    public static readonly Rgb GreenDark = new(0x6F, 0xAF, 0x84);

    /// <summary>Amber for "marginal" and warnings: red 0.78, green 0.52, blue 0.10.</summary>
    public static readonly Rgb Amber = new(0xC7, 0x85, 0x1A);

    /// <summary>Brick red for "no-go" and errors: red 0.69, green 0.23, blue 0.20.</summary>
    public static readonly Rgb Brick = new(0xB0, 0x3B, 0x33);

    /// <summary>Colors per display in plots, forest first (Mac seriesColors).</summary>
    public static readonly IReadOnlyList<Rgb> Series =
    [
        Forest,
        new(0xD9, 0x78, 0x21),
        new(0x3D, 0x6E, 0xA8),
        new(0x8F, 0x59, 0x9E),
        new(0xB3, 0x99, 0x26),
        new(0x73, 0x73, 0x73),
    ];

    /// <summary>Window surface in a light appearance: the Mac's window gray.</summary>
    public static readonly Rgb WindowLight = new(0xEC, 0xEC, 0xEC);

    /// <summary>Window surface in a dark appearance: the Mac's window gray.</summary>
    public static readonly Rgb WindowDark = new(0x1E, 0x1E, 0x1E);

    /// <summary>Sway green: forest in light, the lighter green in dark (SPEC F24). The mark, the glow, the default accent.</summary>
    public static Rgb Green(bool light) => light ? Forest : GreenDark;

    /// <summary>Accent for text and glyphs: forest in light, sage in dark.</summary>
    public static Rgb TextAccent(bool light) => light ? Forest : Sage;

    /// <summary>The color for the display at <paramref name="index"/> in layout order (wraps around).</summary>
    public static Rgb SeriesColor(int index) => Series[((index % Series.Count) + Series.Count) % Series.Count];

    // ----- Spacing and radius (Mac layout values, in device independent pixels) -----

    /// <summary>Spacing scale.</summary>
    public static class Space
    {
        public const double Hairline = 0.5;
        public const double Xs = 2;
        public const double Sm = 4;
        public const double Md = 6;
        public const double Lg = 8;
        public const double Xl = 12;

        /// <summary>Space between cards and sections in Settings.</summary>
        public const double Section = 16;

        /// <summary>Padding of a card's content.</summary>
        public const double Card = 12;

        /// <summary>Padding around the Settings content.</summary>
        public const double SettingsPadding = 20;

        /// <summary>Padding around the Setup content.</summary>
        public const double SetupPadding = 24;
    }

    /// <summary>Corner radii.</summary>
    public static class Radius
    {
        /// <summary>Cards.</summary>
        public const double Card = 10;

        /// <summary>The shortcut chip and buttons.</summary>
        public const double Chip = 6;

        /// <summary>A hovered menu row.</summary>
        public const double Row = 5;

        /// <summary>The welcome tile's radius as a share of its side (the app icon's squircle).</summary>
        public const double TileRatio = 0.225;
    }

    /// <summary>Sizes of windows and marks.</summary>
    public static class Size
    {
        public const double SettingsWidth = 520;
        public const double SetupWidth = 520;
        public const double SetupHeight = 420;
        public const double MenuWidth = 300;
        public const double HeaderMark = 16;
        public const double SetupMark = 24;
        public const double WelcomeTile = 64;
        public const double WelcomeTextWidth = 400;
        public const double StatusDot = 8;
    }

    // ----- Font (SF Pro cannot ship; Segoe UI Variable is in the box on Windows 11) -----

    /// <summary>Body and small text.</summary>
    public static readonly FontStack Text = new("Segoe UI Variable Text", "Segoe UI");

    /// <summary>Large text (the display optical size).</summary>
    public static readonly FontStack Display = new("Segoe UI Variable Display", "Segoe UI");

    /// <summary>Monospaced text (diagnostics).</summary>
    public static readonly FontStack Mono = new("Cascadia Mono", "Consolas");

    /// <summary>The Windows type ramp laid over the Mac hierarchy: caption, callout, body, headline, title3, title2, title.</summary>
    public static class Type
    {
        /// <summary>Mac caption.</summary>
        public static readonly TextStyle Caption = new(12, 400);

        /// <summary>Mac callout: helper text and banners.</summary>
        public static readonly TextStyle Callout = new(13, 400);

        /// <summary>Mac body.</summary>
        public static readonly TextStyle Body = new(14, 400);

        /// <summary>Mac headline: a section title.</summary>
        public static readonly TextStyle Headline = new(14, 600);

        /// <summary>Mac title3: the welcome line and the look instruction.</summary>
        public static readonly TextStyle Title3 = new(16, 400);

        /// <summary>Mac title3 semibold.</summary>
        public static readonly TextStyle Title3Strong = new(16, 600);

        /// <summary>Mac title2 semibold: a Setup step title.</summary>
        public static readonly TextStyle Title2 = new(20, 600, true);

        /// <summary>Mac title semibold: the welcome title.</summary>
        public static readonly TextStyle Title = new(24, 600, true);

        /// <summary>The shortcut chip (Mac title3 medium).</summary>
        public static readonly TextStyle Chip = new(16, 500);

        /// <summary>The notice pill (Mac 15 pt medium).</summary>
        public static readonly TextStyle Notice = new(16, 500);

        /// <summary>The stack for a style: display sizes use the display font.</summary>
        public static FontStack StackFor(TextStyle style) => style.Display ? Display : Text;
    }

    // ----- Motion (Mac numbers) -----

    /// <summary>Durations in seconds, and the easing.</summary>
    public static class Motion
    {
        /// <summary>A banner or a Setup step fading: about 0.2 s, none with reduced motion.</summary>
        public const double Fade = 0.2;

        /// <summary>The edge glow: fade in 0.1 s, hold 0.05 s, fade out 0.2 s; 0.35 s without fading.</summary>
        public static readonly FeedbackTiming Glow = new(0.1, 0.05, 0.2, 0.35);

        /// <summary>The Focus Lock notice: fade in 0.15 s, hold 1 s, fade out 0.3 s; 1.3 s without fading.</summary>
        public static readonly FeedbackTiming Notice = new(0.15, 1, 0.3, 1.3);

        /// <summary>The "Look here" marker fades over this long.</summary>
        public const double LookFade = 0.4;

        /// <summary>ease in, ease out: the Mac's CAMediaTimingFunction easeInEaseOut, as a cubic Bezier (x1, y1, x2, y2).</summary>
        public static readonly (double X1, double Y1, double X2, double Y2) EaseInOut = (0.42, 0, 0.58, 1);
    }

    // ----- Feedback on the displays -----

    /// <summary>The edge glow (Mac EdgeGlowView).</summary>
    public static class Glow
    {
        /// <summary>Width of the solid border.</summary>
        public const double BorderWidth = 4;

        /// <summary>Width of the soft glow inside the border.</summary>
        public const double GlowWidth = 10;

        /// <summary>Opacity of the glow next to the border.</summary>
        public const double GlowOpacity = 0.4;

        /// <summary>Opacity of glow ring <paramref name="index"/> (0 touches the border), falling off quadratically.</summary>
        public static double RingOpacity(int index)
        {
            var position = (index + 0.5) / GlowWidth;
            return GlowOpacity * (1 - position) * (1 - position);
        }

        /// <summary>The gradient stops from the display edge inward: one per ring, then clear. Offsets 0 to 1.</summary>
        public static IReadOnlyList<(double Offset, double Opacity)> Stops()
        {
            var rings = (int)GlowWidth;
            return Enumerable.Range(0, rings + 1)
                .Select(i => ((double)i / rings, i < rings ? RingOpacity(i) : 0.0))
                .ToList();
        }
    }

    /// <summary>The Focus Lock notice's layout (Mac FeedbackLayout and NoticePillView).</summary>
    public static class Notice
    {
        /// <summary>Distance from the top of the work area to the top of the notice.</summary>
        public const double TopOffset = 48;

        /// <summary>Horizontal padding inside the pill.</summary>
        public const double PaddingX = 16;

        /// <summary>Vertical padding inside the pill.</summary>
        public const double PaddingY = 9;
    }
}
