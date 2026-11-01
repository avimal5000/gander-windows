using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Gander.Core;
using Gander.Core.Platform;

namespace Gander.App.Presentation;

/// <summary>One saved calibration in the Settings list.</summary>
public sealed record CalibrationRow(CalibrationKey Key, string Displays, string Camera, DateTimeOffset Date, bool IsInUse)
{
    /// <summary>"Camera · 12 Oct 2026, 14:30".</summary>
    public string Detail => $"{Camera} · {Date.LocalDateTime.ToString("d MMM yyyy, HH:mm", CultureInfo.InvariantCulture)}";
}

/// <summary>The words of the Settings window: the Mac's sections, labels and helper text, with Windows terms. Pure.</summary>
public static class SettingsWording
{
    // Sections, in the Mac's order. "About" is Windows only.
    public const string Appearance = "Appearance";
    public const string General = "General";
    public const string Switching = "Switching";
    public const string ShortcutSection = "Focus Lock shortcut";
    public const string CameraSection = "Camera";
    public const string Calibrations = "Calibrations";
    public const string About = "About";

    /// <summary>The sections top to bottom.</summary>
    public static IReadOnlyList<string> SectionOrder { get; } =
        [Appearance, General, Switching, ShortcutSection, CameraSection, Calibrations, About];

    // Appearance
    public const string AccentLabel = "Accent color";
    public const string GlowToggle = "Glow on the display that gets focus";
    public const string NoticeToggle = "Show a notice when the shortcut toggles Focus Lock";
    public const string GlowHelp = "A soft glow at the edge of the display that just got keyboard focus, so you can see where your typing will go.";
    public const string NoticeHelp = "A small \"Focus Lock on\" or \"Focus Lock off\" notice, so you know the shortcut worked.";

    // General
    public const string StartWithWindows = "Start with Windows";
    public const string StartWithWindowsHelp = "Opens Gander in the system tray when you sign in to Windows.";
    public const string StartWithWindowsUnavailable = "Starting with Windows is not available for this copy of Gander.";

    // Switching
    public const string HoldTime = "Hold time";
    public const string HoldTimeHelp = "How long you look at a display before Gander switches to it.";
    public const string Pause = "Pause after a switch";

    public const string PauseHelp =
        "How long Gander waits after switching before it will switch again. Shorter is faster for a quick look back, but can bounce.";

    public const string Confidence = "Switch confidence";
    public const string ConfidenceHelp = "A stricter setting needs a clearer look at one display before Gander switches.";
    public const string ConfidenceMin = "Relaxed";
    public const string ConfidenceMax = "Strict";
    public const string UseInstant = "Use Instant timing";
    public const string RestoreDefaults = "Restore Defaults";

    /// <summary>The five confidence steps, "Relaxed" to "Strict".</summary>
    public static IReadOnlyList<string> ConfidenceNames { get; } = ["Relaxed", "Easy", "Balanced", "Firm", "Strict"];

    // Focus Lock shortcut
    public const string RecordShortcut = "Record Shortcut";
    public const string Cancel = "Cancel";
    public const string Clear = "Clear";
    public const string TypeShortcut = "Type a shortcut…";
    public const string FocusLockExplained =
        "Focus Lock freezes switching: keyboard focus stays where it is, wherever you look, until you turn it off. Handy while you read one display and type on another.";

    public const string ShortcutHelp = "Works from any app. A shortcut needs Ctrl, Alt, or Win. Esc cancels recording.";

    // Camera
    public const string DefaultCamera = "Default camera";
    public const string UnavailableCamera = "Unavailable camera (uses default)";

    // Calibrations
    public const string NoCalibrations = "No calibrations yet.";
    public const string InUse = "In use";
    public const string Delete = "Delete";
    public const string Calibrate = "Calibrate…";
    public const string UnknownCamera = "Unknown camera";

    /// <summary>The confidence step's name; out of range levels use the nearest step.</summary>
    public static string ConfidenceName(int level) => ConfidenceNames[Math.Min(Math.Max(level, 0), ConfidenceNames.Count - 1)];

    /// <summary>"Reset to Ctrl+Alt+Shift+L".</summary>
    public static string ResetTo(HotKeyShortcut shortcut) => $"Reset to {shortcut.DisplayString}";

    /// <summary>The tooltip of "Use Instant timing".</summary>
    public static string InstantHelp =>
        $"Hold time {TimingPresets.Seconds(SwitchSensitivity.Instant.Dwell)} and pause "
        + $"{TimingPresets.Seconds(SwitchSensitivity.Instant.Cooldown)}. Switch confidence stays as it is.";

    /// <summary>The Mac's one row per saved profile, newest first as the library keeps them.</summary>
    public static IReadOnlyList<CalibrationRow> Rows(CalibrationLibrary library, CalibrationKey? activeKey, Func<DisplayId, string> name) =>
        library.Profiles
            .Select(profile => new CalibrationRow(
                profile.Key,
                string.Join(", ", profile.Displays.Select(d => name(d.DisplayId))),
                profile.CameraName ?? UnknownCamera,
                profile.CreatedAt,
                activeKey is not null && profile.Key.Equals(activeKey)))
            .ToList();

    /// <summary>The Focus Lock notice text: "Focus Lock on" or "Focus Lock off".</summary>
    public static string NoticeText(bool isOn) => isOn ? "Focus Lock on" : "Focus Lock off";
}
