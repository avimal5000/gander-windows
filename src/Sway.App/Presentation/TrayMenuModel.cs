using System.Collections.Generic;
using System.Linq;
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
    ToggleLoginItem,
    Quit,
}

/// <summary>One row of the tray menu (pure data; the ContextMenuStrip is built from a list of these).</summary>
public sealed record TrayMenuEntry(
    string Text,
    TrayCommand Command = TrayCommand.None,
    bool Enabled = true,
    bool Checked = false,
    bool IsSeparator = false,
    string? Argument = null,
    IReadOnlyList<TrayMenuEntry>? Children = null)
{
    /// <summary>A separator line.</summary>
    public static TrayMenuEntry Separator { get; } = new(string.Empty, IsSeparator: true);
}

/// <summary>Everything the menu depends on, copied from the controller and the platform when the menu opens.</summary>
public sealed record TrayMenuInput(
    string StatusLine,
    bool TrackingEnabled,
    bool FocusLocked,
    HotKeyShortcut? FocusLockShortcut,
    string? ShortcutMessage,
    IReadOnlyList<CameraDevice> Cameras,
    string? SelectedCameraId,
    LoginItemStatus LoginItem);

/// <summary>Builds the tray menu from state. Pure, so the order, the check marks and the wording are tested.</summary>
public static class TrayMenuModel
{
    /// <summary>The menu, top to bottom.</summary>
    public static IReadOnlyList<TrayMenuEntry> Build(TrayMenuInput input)
    {
        var entries = new List<TrayMenuEntry>
        {
            new(input.StatusLine, Enabled: false),
            TrayMenuEntry.Separator,
            new("Tracking", TrayCommand.ToggleTracking, Checked: input.TrackingEnabled),
            new(FocusLockText(input.FocusLockShortcut), TrayCommand.ToggleFocusLock, Checked: input.FocusLocked),
        };

        if (!string.IsNullOrEmpty(input.ShortcutMessage))
        {
            entries.Add(new TrayMenuEntry(input.ShortcutMessage, Enabled: false));
        }

        entries.Add(TrayMenuEntry.Separator);
        entries.Add(CameraMenu(input));
        entries.Add(new TrayMenuEntry("Settings…", TrayCommand.Settings));
        entries.Add(new TrayMenuEntry("Calibrate…", TrayCommand.Calibrate));
        entries.Add(new TrayMenuEntry("Diagnostics…", TrayCommand.Diagnostics));
        entries.Add(TrayMenuEntry.Separator);
        entries.Add(new TrayMenuEntry(
            "Open Sway at login",
            TrayCommand.ToggleLoginItem,
            Enabled: input.LoginItem != LoginItemStatus.Unavailable,
            Checked: input.LoginItem == LoginItemStatus.Enabled));
        entries.Add(new TrayMenuEntry("Quit Sway", TrayCommand.Quit));
        return entries;
    }

    /// <summary>"Focus Lock (Ctrl+Alt+Shift+L)", or plain "Focus Lock" when no shortcut is set.</summary>
    public static string FocusLockText(HotKeyShortcut? shortcut) =>
        shortcut is { IsValid: true } ? $"Focus Lock ({shortcut.DisplayString})" : "Focus Lock";

    private static TrayMenuEntry CameraMenu(TrayMenuInput input)
    {
        if (input.Cameras.Count == 0)
        {
            return new TrayMenuEntry("Camera", Children: [new TrayMenuEntry("No camera found", Enabled: false)]);
        }

        var children = input.Cameras
            .Select(camera => new TrayMenuEntry(
                camera.Name,
                TrayCommand.SelectCamera,
                Checked: camera.Id == input.SelectedCameraId,
                Argument: camera.Id))
            .ToList();
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
