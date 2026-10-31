using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Sway.App.Presentation;

/// <summary>One camera in the diagnostics.</summary>
public sealed record DiagnosticsCamera(string Name, bool IsBuiltIn, bool IsSelected, bool IsRunning);

/// <summary>One monitor in the diagnostics. <see cref="ScalePercent"/> is null when Windows would not say.</summary>
public sealed record DiagnosticsDisplay(string Name, int Width, int Height, int? ScalePercent, bool IsMain, bool IsFocused);

/// <summary>Everything the diagnostics show, as plain values, so the text is built without a controller.</summary>
public sealed record DiagnosticsSnapshot
{
    /// <summary>Sway's version.</summary>
    public string SwayVersion { get; init; } = "unknown";

    /// <summary>For example "Windows 11 25H2, build 26200.1234".</summary>
    public string WindowsVersion { get; init; } = "unknown";

    /// <summary>For example "X64 (process X64)".</summary>
    public string Architecture { get; init; } = "unknown";

    /// <summary>The status line shown in the tray.</summary>
    public string StatusLine { get; init; } = string.Empty;

    /// <summary>Whether Tracking is on.</summary>
    public bool TrackingEnabled { get; init; }

    /// <summary>Whether Focus Lock is on.</summary>
    public bool FocusLocked { get; init; }

    /// <summary>The camera state in words, for example "Running".</summary>
    public string CaptureText { get; init; } = string.Empty;

    /// <summary>The wording of the hold that stops automatic switching right now, or null.</summary>
    public string? HoldText { get; init; }

    /// <summary>The camera in use or about to be used.</summary>
    public string? CurrentCamera { get; init; }

    /// <summary>The cameras the system offers.</summary>
    public IReadOnlyList<DiagnosticsCamera> Cameras { get; init; } = [];

    /// <summary>The connected monitors, left to right.</summary>
    public IReadOnlyList<DiagnosticsDisplay> Displays { get; init; } = [];

    /// <summary>True when a saved calibration fits the displays and the camera.</summary>
    public bool HasCalibration { get; init; }

    /// <summary>When the matching calibration was made.</summary>
    public DateTimeOffset? CalibrationCreatedAt { get; init; }

    /// <summary>True when the calibration covers more displays than are connected.</summary>
    public bool CalibrationPartial { get; init; }

    /// <summary>The time the snapshot was taken, to work out the calibration's age.</summary>
    public DateTimeOffset Now { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>How many calibrations are saved in the library.</summary>
    public int LibraryCount { get; init; }

    /// <summary>Seconds a look must be held.</summary>
    public double DwellSeconds { get; init; }

    /// <summary>Seconds between switches.</summary>
    public double CooldownSeconds { get; init; }

    /// <summary>The switch confidence level (0 to 4).</summary>
    public int ConfidenceLevel { get; init; }

    /// <summary>The saved settings as key and value; long values are shortened.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Settings { get; init; } = [];

    /// <summary>The recent switches, newest last, as "time text" lines.</summary>
    public IReadOnlyList<string> SwitchLines { get; init; } = [];

    /// <summary>The recent controller log lines, newest last.</summary>
    public IReadOnlyList<string> LogLines { get; init; } = [];
}

/// <summary>Builds the plain-text block shown in the Diagnostics window and copied by "Copy diagnostics".</summary>
public static class DiagnosticsText
{
    /// <summary>How many log lines the block keeps.</summary>
    public const int MaxLogLines = 40;

    /// <summary>Longest saved value shown in full.</summary>
    public const int MaxSettingValue = 120;

    /// <summary>The text block. Contains no window titles and no image data: the snapshot cannot carry either.</summary>
    public static string Build(DiagnosticsSnapshot s)
    {
        var text = new StringBuilder();
        text.AppendLine("Sway diagnostics");
        text.AppendLine($"Sway version: {s.SwayVersion}");
        text.AppendLine($"Windows: {s.WindowsVersion}");
        text.AppendLine($"Architecture: {s.Architecture}");
        text.AppendLine();

        text.AppendLine("== Status ==");
        text.AppendLine($"Status: {s.StatusLine}");
        text.AppendLine($"Tracking: {OnOff(s.TrackingEnabled)}");
        text.AppendLine($"Focus Lock: {OnOff(s.FocusLocked)}");
        text.AppendLine($"Camera state: {s.CaptureText}");
        text.AppendLine($"Holds: {s.HoldText ?? "none"}");
        text.AppendLine("Live pose (yaw, pitch): not available yet");
        // LIVE-POSE-PLACEHOLDER: replace the line above with yaw and pitch once the controller exposes them.
        text.AppendLine();

        text.AppendLine("== Cameras ==");
        text.AppendLine($"Current camera: {s.CurrentCamera ?? "none"}");
        foreach (var camera in s.Cameras)
        {
            var notes = new List<string>();
            if (camera.IsBuiltIn)
            {
                notes.Add("built in");
            }

            if (camera.IsSelected)
            {
                notes.Add("selected");
            }

            if (camera.IsRunning)
            {
                notes.Add("running");
            }

            text.AppendLine($"- {camera.Name}{(notes.Count > 0 ? $" ({string.Join(", ", notes)})" : string.Empty)}");
        }

        if (s.Cameras.Count == 0)
        {
            text.AppendLine("(none found)");
        }

        text.AppendLine();

        text.AppendLine($"== Monitors ({s.Displays.Count}), left to right ==");
        for (var i = 0; i < s.Displays.Count; i++)
        {
            var d = s.Displays[i];
            var scale = d.ScalePercent is { } percent ? $"{percent}%" : "unknown";
            text.AppendLine(
                $"{i + 1}. {d.Name}: {d.Width}x{d.Height}, scale {scale}{(d.IsMain ? ", primary" : string.Empty)}{(d.IsFocused ? ", has focus" : string.Empty)}");
        }

        text.AppendLine();

        text.AppendLine("== Calibration ==");
        text.AppendLine(CalibrationLine(s));
        text.AppendLine($"Saved calibrations: {s.LibraryCount}");
        text.AppendLine();

        text.AppendLine("== Sensitivity ==");
        text.AppendLine(string.Create(
            CultureInfo.InvariantCulture,
            $"Hold time: {s.DwellSeconds:0.00} s, cooldown: {s.CooldownSeconds:0.00} s, confidence level: {s.ConfidenceLevel}"));
        text.AppendLine();

        text.AppendLine("== Saved settings ==");
        foreach (var (key, value) in s.Settings)
        {
            text.AppendLine($"{key} = {ShortValue(value)}");
        }

        if (s.Settings.Count == 0)
        {
            text.AppendLine("(nothing saved)");
        }

        text.AppendLine();

        text.AppendLine("== Recent switches ==");
        AppendLines(text, s.SwitchLines, MaxLogLines);
        text.AppendLine();
        text.AppendLine("== Log ==");
        AppendLines(text, s.LogLines, MaxLogLines);
        return text.ToString().TrimEnd();
    }

    /// <summary>A saved value, shortened with its length when long (the calibration library is a few thousand characters).</summary>
    public static string ShortValue(string value) =>
        value.Length <= MaxSettingValue ? value : $"({value.Length} characters)";

    /// <summary>"3 days" style age, in the largest sensible unit.</summary>
    public static string Age(TimeSpan age)
    {
        if (age < TimeSpan.Zero)
        {
            age = TimeSpan.Zero;
        }

        if (age.TotalMinutes < 1)
        {
            return "under a minute";
        }

        if (age.TotalHours < 1)
        {
            return Plural((int)age.TotalMinutes, "minute");
        }

        return age.TotalDays < 1 ? Plural((int)age.TotalHours, "hour") : Plural((int)age.TotalDays, "day");
    }

    private static string CalibrationLine(DiagnosticsSnapshot s)
    {
        if (!s.HasCalibration)
        {
            return "Calibration for these monitors and this camera: none";
        }

        var age = s.CalibrationCreatedAt is { } created ? $", made {Age(s.Now - created)} ago" : string.Empty;
        var partial = s.CalibrationPartial ? " (covers monitors that are not connected now)" : string.Empty;
        return $"Calibration for these monitors and this camera: present{age}{partial}";
    }

    private static void AppendLines(StringBuilder text, IReadOnlyList<string> lines, int max)
    {
        if (lines.Count == 0)
        {
            text.AppendLine("(none)");
            return;
        }

        foreach (var line in lines.Skip(Math.Max(0, lines.Count - max)))
        {
            text.AppendLine(line);
        }
    }

    private static string OnOff(bool on) => on ? "on" : "off";

    private static string Plural(int count, string unit) => $"{count} {unit}{(count == 1 ? string.Empty : "s")}";
}

/// <summary>Turns the registry's Windows version values into one line (pure, so the Windows 11 rule is tested).</summary>
public static class WindowsVersionText
{
    /// <summary>
    /// Windows 11 still reports "Windows 10" as its ProductName, so builds from 22000 up are named Windows 11.
    /// </summary>
    public static string Format(string? productName, string? displayVersion, string? build, string? revision)
    {
        var name = productName ?? "Windows";
        if (int.TryParse(build, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number >= 22000)
        {
            name = name.Replace("Windows 10", "Windows 11", StringComparison.Ordinal);
        }

        var version = string.IsNullOrEmpty(displayVersion) ? string.Empty : $" {displayVersion}";
        var buildText = string.IsNullOrEmpty(build) ? "unknown" : string.IsNullOrEmpty(revision) ? build : $"{build}.{revision}";
        return $"{name}{version}, build {buildText}";
    }
}
