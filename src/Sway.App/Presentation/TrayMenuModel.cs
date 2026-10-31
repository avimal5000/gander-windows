using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sway.Core;
using Sway.Core.Calibration;
using Sway.Core.Controller;
using Sway.Core.Platform;

namespace Sway.App.Presentation;

/// <summary>What choosing a menu entry does. The tray host maps each to a call on the controller or the app.</summary>
public enum TrayCommand
{
    None,
    ToggleTracking,
    ToggleFocusLock,
    SelectCamera,
    Calibrate,
    Settings,
    Diagnostics,
    OpenCameraSettings,
    Quit,
}

/// <summary>How the host draws a row.</summary>
public enum TrayEntryRole
{
    /// <summary>An ordinary row.</summary>
    Normal,

    /// <summary>The Swing mark and "Sway" at the top.</summary>
    Header,

    /// <summary>The status line: a colored dot and the text.</summary>
    Status,

    /// <summary>A line of a banner: secondary text, never clickable.</summary>
    Banner,

    /// <summary>The "Looking at" line: secondary text.</summary>
    Look,
}

/// <summary>One row of the tray menu (pure data; the ContextMenuStrip is built from a list of these).</summary>
public sealed record TrayMenuEntry(
    string Text,
    TrayCommand Command = TrayCommand.None,
    bool Enabled = true,
    bool Checked = false,
    bool IsSeparator = false,
    string? Argument = null,
    IReadOnlyList<TrayMenuEntry>? Children = null,
    TrayEntryRole Role = TrayEntryRole.Normal,
    string? ShortcutText = null,
    StatusTone? Tone = null)
{
    /// <summary>A separator line.</summary>
    public static TrayMenuEntry Separator { get; } = new(string.Empty, IsSeparator: true);
}

/// <summary>The one banner the menu can show above the switches (Mac MenuBanner). Pure, so the order and words are tested.</summary>
public sealed record TrayBanner(string Text, string? ButtonText, TrayCommand Command)
{
    /// <summary>"Sway needs a quick setup, about a minute."</summary>
    public const string FirstSetupText = "Sway needs a quick setup, about a minute.";

    /// <summary>"New display setup. Calibrate so Sway can switch between them."</summary>
    public const string NeedsCalibrationText = "New display setup. Calibrate so Sway can switch between them.";

    /// <summary>The button of both needs-calibration banners.</summary>
    public const string CalibrateButton = "Calibrate (about 20 s)…";

    /// <summary>"No calibration for &lt;camera&gt; yet."</summary>
    public static string NeedsCameraCalibrationText(string camera) => $"No calibration for {camera} yet.";

    /// <summary>What the menu facts say to show, in priority order: at most one banner.</summary>
    public static TrayBanner? Current(TrayBannerFacts facts)
    {
        if (facts.PermissionPrompt is { } prompt)
        {
            return prompt == CameraPermissionPrompt.Denied
                ? new TrayBanner("Camera access is off. " + SetupPresentation.CameraDeniedMessage, "Open Windows Settings", TrayCommand.OpenCameraSettings)
                : new TrayBanner("Camera access is restricted. " + SetupPresentation.CameraRestrictedMessage, "Open Windows Settings", TrayCommand.OpenCameraSettings);
        }

        if (facts.LibraryIsEmpty)
        {
            return new TrayBanner(FirstSetupText, "Set Up…", TrayCommand.Calibrate);
        }

        if (facts.TrackingEnabled && facts.DisplayCount >= 2 && !facts.HasCalibration)
        {
            return new TrayBanner(
                facts.CameraWithoutCalibration is { } camera ? NeedsCameraCalibrationText(camera) : NeedsCalibrationText,
                CalibrateButton,
                TrayCommand.Calibrate);
        }

        return facts.ShortcutMessage is { Length: > 0 } message ? new TrayBanner(message, null, TrayCommand.None) : null;
    }
}

/// <summary>What the banner decision depends on.</summary>
public sealed record TrayBannerFacts(
    CameraPermissionPrompt? PermissionPrompt,
    bool LibraryIsEmpty,
    bool TrackingEnabled,
    int DisplayCount,
    bool HasCalibration,
    string? ShortcutMessage,
    string? CameraWithoutCalibration = null);

/// <summary>What the "Looking at" line says (Mac LookSummary): one short line.</summary>
public static class LookSummary
{
    /// <summary>The line when the camera is off or nothing classifies the look: an em dash.</summary>
    public const string Off = "\u2014";

    /// <summary>The line for the latest classification; <paramref name="cameraActive"/> false gives <see cref="Off"/>.</summary>
    public static string Text(bool cameraActive, PoseClassification? classification, System.Func<DisplayId, string> label)
    {
        if (!cameraActive || classification is null)
        {
            return Off;
        }

        return classification.Kind switch
        {
            PoseClassification.ClassKind.NoFace => "No face",
            PoseClassification.ClassKind.Elsewhere => "Looking elsewhere",
            _ => classification.Best is { } best ? $"Looking at {label(best)}" : "No face",
        };
    }
}

/// <summary>Everything the menu depends on, copied from the controller and the platform when the menu opens.</summary>
public sealed record TrayMenuInput(
    string StatusLine,
    StatusTone Tone,
    bool TrackingEnabled,
    bool FocusLocked,
    HotKeyShortcut? FocusLockShortcut,
    TrayBanner? Banner,
    string LookText,
    IReadOnlyList<CameraDevice> Cameras,
    string? SelectedCameraId);

/// <summary>
/// Builds the tray menu from state, in the Mac popover's order: the mark and name, the status line, at most one banner,
/// the Tracking and Focus Lock switches, the "Looking at" line, the camera picker, then Calibrate, Settings,
/// Diagnostics and Quit. Pure, so the order, the check marks and the wording are tested.
/// </summary>
public static class TrayMenuModel
{
    /// <summary>Widest line of a banner, in characters, before it continues on the next row.</summary>
    public const int BannerWidth = 46;

    /// <summary>The camera row that means "whatever Windows picks".</summary>
    public const string DefaultCameraText = SettingsWording.DefaultCamera;

    /// <summary>The menu, top to bottom.</summary>
    public static IReadOnlyList<TrayMenuEntry> Build(TrayMenuInput input)
    {
        var entries = new List<TrayMenuEntry>
        {
            new("Sway", Enabled: false, Role: TrayEntryRole.Header),
            new(input.StatusLine, Enabled: false, Role: TrayEntryRole.Status, Tone: input.Tone),
        };

        if (input.Banner is { } banner)
        {
            entries.AddRange(Wrap(banner.Text, BannerWidth).Select(line => new TrayMenuEntry(line, Enabled: false, Role: TrayEntryRole.Banner)));
            if (banner.ButtonText is { } button)
            {
                entries.Add(new TrayMenuEntry(button, banner.Command));
            }
        }

        entries.Add(TrayMenuEntry.Separator);
        entries.Add(new TrayMenuEntry("Tracking", TrayCommand.ToggleTracking, Checked: input.TrackingEnabled));
        entries.Add(new TrayMenuEntry(
            "Focus Lock",
            TrayCommand.ToggleFocusLock,
            Checked: input.FocusLocked,
            ShortcutText: input.FocusLockShortcut is { IsValid: true } shortcut ? shortcut.DisplayString : null));
        entries.Add(new TrayMenuEntry(input.LookText, Enabled: false, Role: TrayEntryRole.Look));
        entries.Add(TrayMenuEntry.Separator);
        entries.Add(CameraMenu(input));
        entries.Add(TrayMenuEntry.Separator);
        entries.Add(new TrayMenuEntry("Calibrate…", TrayCommand.Calibrate));
        entries.Add(new TrayMenuEntry("Settings…", TrayCommand.Settings));
        entries.Add(new TrayMenuEntry("Diagnostics…", TrayCommand.Diagnostics));
        entries.Add(TrayMenuEntry.Separator);
        entries.Add(new TrayMenuEntry("Quit Sway", TrayCommand.Quit));
        return entries;
    }

    /// <summary>"Focus Lock"; the shortcut is shown beside it, right aligned, not in the text.</summary>
    public static string FocusLockText => "Focus Lock";

    /// <summary>Breaks <paramref name="text"/> at spaces into lines of at most <paramref name="width"/> characters.</summary>
    public static IReadOnlyList<string> Wrap(string text, int width)
    {
        var lines = new List<string>();
        var line = new StringBuilder();
        foreach (var word in text.Split(' ', System.StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length > 0 && line.Length + 1 + word.Length > width)
            {
                lines.Add(line.ToString());
                line.Clear();
            }

            if (line.Length > 0)
            {
                line.Append(' ');
            }

            line.Append(word);
        }

        if (line.Length > 0)
        {
            lines.Add(line.ToString());
        }

        return lines;
    }

    private static TrayMenuEntry CameraMenu(TrayMenuInput input)
    {
        if (input.Cameras.Count == 0)
        {
            return new TrayMenuEntry("Camera", Children: [new TrayMenuEntry("No camera found", Enabled: false)]);
        }

        var children = new List<TrayMenuEntry>
        {
            new(DefaultCameraText, TrayCommand.SelectCamera, Checked: input.SelectedCameraId is null),
        };
        children.AddRange(input.Cameras.Select(camera => new TrayMenuEntry(
            camera.Name,
            TrayCommand.SelectCamera,
            Checked: camera.Id == input.SelectedCameraId,
            Argument: camera.Id)));
        if (input.SelectedCameraId is { } id && input.Cameras.All(c => c.Id != id))
        {
            children.Add(new TrayMenuEntry(SettingsWording.UnavailableCamera, TrayCommand.SelectCamera, Checked: true, Argument: id));
        }

        return new TrayMenuEntry("Camera", Children: children);
    }
}

/// <summary>
/// Decides whether Sway counts as "in front" (SPEC D28): its tray menu is open or one of its windows is active.
/// Each source reports its own state; the answer is true while any is set.
/// </summary>
public sealed class InFrontTracker
{
    private readonly HashSet<string> _sources = [];

    /// <summary>True while any source is in front.</summary>
    public bool IsInFront => _sources.Count > 0;

    /// <summary>Records that a source is in front or not. Returns the new overall answer.</summary>
    public bool Set(string source, bool inFront)
    {
        if (inFront)
        {
            _sources.Add(source);
        }
        else
        {
            _sources.Remove(source);
        }

        return IsInFront;
    }
}
