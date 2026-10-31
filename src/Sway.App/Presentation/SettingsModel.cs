using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Sway.Core;
using Sway.Core.Platform;

namespace Sway.App.Presentation;

/// <summary>A named hold time and pause (SPEC D49). Confidence is not part of a preset.</summary>
public sealed record TimingPreset(string Name, double Dwell, double Cooldown);

/// <summary>The switching timing presets and the slider arithmetic. Pure.</summary>
public static class TimingPresets
{
    /// <summary>Slow, Default, Quick, Instant: longest to shortest. Default and Instant come from <see cref="SwitchSensitivity"/>.</summary>
    public static IReadOnlyList<TimingPreset> All { get; } =
    [
        new("Slow", 0.70, 1.00),
        new("Default", SwitchSensitivity.Default.Dwell, SwitchSensitivity.Default.Cooldown),
        new("Quick", 0.25, 0.50),
        new("Instant", SwitchSensitivity.Instant.Dwell, SwitchSensitivity.Instant.Cooldown),
    ];

    /// <summary>The preset whose hold time and pause equal the current ones (to the slider step), or null for a custom mix.</summary>
    public static TimingPreset? Match(SwitchSensitivity value) =>
        All.FirstOrDefault(p => Same(p.Dwell, value.Dwell) && Same(p.Cooldown, value.Cooldown));

    /// <summary>The preset applied to <paramref name="current"/>: new timing, same confidence.</summary>
    public static SwitchSensitivity Apply(TimingPreset preset, SwitchSensitivity current) =>
        new(preset.Dwell, preset.Cooldown, current.ConfidenceLevel);

    /// <summary>A slider value as seconds on the 0.05 s grid.</summary>
    public static double Snap(double seconds) => Math.Round(Math.Round(seconds / 0.05) * 0.05, 2);

    /// <summary>"0.40 s".</summary>
    public static string Seconds(double seconds) => seconds.ToString("0.00", CultureInfo.InvariantCulture) + " s";

    private static bool Same(double a, double b) => Math.Abs(a - b) < 0.025;
}

/// <summary>The outcome of a key press while capturing a Focus Lock shortcut.</summary>
public enum ShortcutCaptureKind
{
    /// <summary>Only modifiers are down so far; keep waiting.</summary>
    Waiting,

    /// <summary>A usable shortcut.</summary>
    Accepted,

    /// <summary>A key without Ctrl, Alt or Win.</summary>
    NeedsModifier,

    /// <summary>A key that cannot be a shortcut (Esc, Tab, Enter and so on).</summary>
    Unsupported,
}

/// <summary>A capture result: the shortcut when accepted, else a hint to show.</summary>
public sealed record ShortcutCapture(ShortcutCaptureKind Kind, HotKeyShortcut? Shortcut, string Hint)
{
    private const uint VkShift = 0x10;
    private const uint VkControl = 0x11;
    private const uint VkMenu = 0x12;
    private const uint VkLWin = 0x5B;
    private const uint VkRWin = 0x5C;

    /// <summary>Judges a key press: <paramref name="virtualKey"/> is the Windows virtual-key code, modifiers are what is held.</summary>
    public static ShortcutCapture From(uint virtualKey, HotKeyModifiers modifiers)
    {
        if (virtualKey is VkShift or VkControl or VkMenu or VkLWin or VkRWin)
        {
            return new(ShortcutCaptureKind.Waiting, null, "Now press the last key.");
        }

        var label = KeyLabel(virtualKey);
        if (label is null)
        {
            return new(ShortcutCaptureKind.Unsupported, null, "That key can't be used. Try a letter, a number or an F key.");
        }

        var shortcut = new HotKeyShortcut(virtualKey, modifiers, label);
        return shortcut.IsValid
            ? new(ShortcutCaptureKind.Accepted, shortcut, string.Empty)
            : new(ShortcutCaptureKind.NeedsModifier, null, "Hold Ctrl, Alt or the Windows key as well.");
    }

    /// <summary>The label of a virtual key, or null when it is not allowed in a shortcut.</summary>
    public static string? KeyLabel(uint virtualKey)
    {
        if (virtualKey is >= 0x41 and <= 0x5A)
        {
            return ((char)virtualKey).ToString();
        }

        if (virtualKey is >= 0x30 and <= 0x39)
        {
            return ((char)virtualKey).ToString();
        }

        if (virtualKey is >= 0x70 and <= 0x87)
        {
            return "F" + (virtualKey - 0x6F).ToString(CultureInfo.InvariantCulture);
        }

        return virtualKey switch
        {
            0x20 => "Space",
            0x21 => "Page Up",
            0x22 => "Page Down",
            0x23 => "End",
            0x24 => "Home",
            0x25 => "Left",
            0x26 => "Up",
            0x27 => "Right",
            0x28 => "Down",
            0x2D => "Insert",
            0x2E => "Delete",
            _ => null,
        };
    }
}

/// <summary>Words for the settings window that depend on state. Pure.</summary>
public static class SettingsText
{
    /// <summary>"Calibrated for 2 displays on 12 Oct 2026" or why it is not.</summary>
    public static string Calibration(int displayCount, DateTimeOffset? createdAt, bool isPartial, bool running)
    {
        if (running)
        {
            return "Calibration is running.";
        }

        if (createdAt is null)
        {
            return "Not calibrated for this camera and these displays yet.";
        }

        var plural = displayCount == 1 ? "display" : "displays";
        var text = $"Calibrated for {displayCount} {plural} on {createdAt.Value.LocalDateTime.ToString("d MMM yyyy", CultureInfo.InvariantCulture)}.";
        return isPartial ? text + " Some of those displays are not connected now." : text;
    }

    /// <summary>The line under the camera picker.</summary>
    public static string CameraHint(int deviceCount) =>
        deviceCount == 0 ? "No camera found." : "Sway uses this camera only while Tracking is on.";

    /// <summary>"Focus Lock shortcut: Ctrl+Alt+Shift+L", or none.</summary>
    public static string ShortcutValue(HotKeyShortcut? shortcut) =>
        shortcut is { IsValid: true } ? shortcut.DisplayString : "None";

    /// <summary>The privacy line in the About block.</summary>
    public const string PrivacyLine = "Camera frames never leave this PC.";

    /// <summary>A short summary of the third-party notices.</summary>
    public const string NoticesSummary =
        "Sway uses ONNX Runtime (MIT) and a face-landmark model from Google MediaPipe (Apache-2.0). " +
        "Full license texts are in THIRD-PARTY-NOTICES.md.";

    /// <summary>"Version 1.2.3", cutting any build metadata after a plus sign.</summary>
    public static string Version(string version)
    {
        var plus = version.IndexOf('+', StringComparison.Ordinal);
        return "Version " + (plus > 0 ? version[..plus] : version);
    }
}

/// <summary>Colors of the tray menu for the app theme (SPEC look: ivory and charcoal, forest or sage accent).</summary>
public sealed record MenuPalette(Rgb Background, Rgb Text, Rgb Disabled, Rgb Hover, Rgb Border, Rgb Separator, Rgb Accent)
{
    /// <summary>Ivory surface with charcoal text, or charcoal surface with ivory text.</summary>
    public static MenuPalette For(bool light) => light
        ? new(Rgb.Hex("#F6F3EA"), Rgb.Hex("#2B2B2B"), Rgb.Hex("#8A877E"), Rgb.Hex("#E6E1D3"), Rgb.Hex("#D8D3C4"), Rgb.Hex("#DAD5C6"), TrayPalette.Forest)
        : new(Rgb.Hex("#2B2B2B"), Rgb.Hex("#F6F3EA"), Rgb.Hex("#8C8A84"), Rgb.Hex("#3C3C3A"), Rgb.Hex("#454542"), Rgb.Hex("#454542"), TrayPalette.Sage);
}
