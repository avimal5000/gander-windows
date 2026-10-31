using System.Text.Json;
using Sway.Core.Platform;

namespace Sway.Core.Controller;

// Focus Lock and its global shortcut (SPEC F17, D27, D7). While Focus Lock is on the camera stays on and nothing
// switches. The shortcut is registered through the injected IHotKeyRegistrar (RegisterHotKey on Windows: no hook), so
// it works from any app. A registration failure is kept as ShortcutMessage, never thrown.
public sealed partial class TrackingController
{
    /// <summary>Turns Focus Lock on or off (the menu switch). Not saved: Focus Lock is off at launch.</summary>
    public void SetFocusLock(bool locked)
    {
        if (locked == FocusLocked)
        {
            return;
        }

        FocusLocked = locked;
        if (locked)
        {
            CancelPendingSwitch();
        }

        SyncPoseDemand();
        Log($"Focus Lock {(locked ? "on" : "off")}");
        Publish();
    }

    /// <summary>Toggles Focus Lock, without any notice.</summary>
    public void ToggleFocusLock() => SetFocusLock(!FocusLocked);

    // The shortcut's action: toggles Focus Lock and tells the UI so it can show the notice on the focused display.
    private void FocusLockShortcutPressed()
    {
        ToggleFocusLock();
        FocusLockToggledByShortcut?.Invoke(FocusLocked);
    }

    /// <summary>Registers the saved shortcut (or the default) at launch. A failure is kept as <see cref="ShortcutMessage"/>.</summary>
    private void RegisterFocusLockShortcut()
    {
        if (FocusLockShortcut is not { } shortcut)
        {
            return;
        }

        try
        {
            _env.HotKeys.Register(shortcut, HotKeyAction.ToggleFocusLock, () => Post(FocusLockShortcutPressed));
            ShortcutMessage = null;
            IsShortcutRegistered = true;
        }
        catch (HotKeyException error)
        {
            ShortcutMessage = ShortcutMessageFor(error, shortcut);
            IsShortcutRegistered = false;
            Log("The Focus Lock shortcut could not be registered");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // For example the platform's message window could not be created: startup must go on without a shortcut.
            ShortcutMessage = "The Focus Lock shortcut could not be set up.";
            IsShortcutRegistered = false;
            Log($"The Focus Lock shortcut could not be registered ({ex.GetType().Name})");
        }
    }

    /// <summary>The menu warns only while the chosen shortcut doesn't work.</summary>
    public string? MenuShortcutMessage => FocusLockShortcut is not null && !IsShortcutRegistered ? ShortcutMessage : null;

    /// <summary>
    /// Replaces the Focus Lock shortcut (null removes it). The new one is registered first; on success it is saved and
    /// the message cleared. On failure the previous shortcut is registered again, nothing is saved, and the message says why.
    /// </summary>
    /// <returns>Whether the new shortcut is now in effect.</returns>
    public bool SetFocusLockShortcut(HotKeyShortcut? shortcut)
    {
        var previous = FocusLockShortcut;
        if (shortcut is null)
        {
            _env.HotKeys.Unregister(HotKeyAction.ToggleFocusLock);
            SaveFocusLockShortcut(null);
            FocusLockShortcut = null;
            ShortcutMessage = null;
            IsShortcutRegistered = false;
            Publish();
            return true;
        }

        try
        {
            _env.HotKeys.Register(shortcut, HotKeyAction.ToggleFocusLock, () => Post(FocusLockShortcutPressed));
        }
        catch (HotKeyException error)
        {
            ShortcutMessage = ShortcutMessageFor(error, shortcut);
            IsShortcutRegistered = Restore(previous);
            Publish();
            return false;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The platform failed in another way (for example its message window is gone): same outcome, no crash.
            ShortcutMessage = "The shortcut could not be set up.";
            IsShortcutRegistered = Restore(previous);
            Log($"The Focus Lock shortcut could not be registered ({ex.GetType().Name})");
            Publish();
            return false;
        }

        SaveFocusLockShortcut(shortcut);
        FocusLockShortcut = shortcut;
        ShortcutMessage = null;
        IsShortcutRegistered = true;
        Publish();
        return true;
    }

    /// <summary>"Reset to Ctrl+Alt+Shift+L".</summary>
    public bool ResetFocusLockShortcut() => SetFocusLockShortcut(HotKeyShortcut.FocusLockDefault);

    // A failed registration leaves the action without a shortcut, so register the previous one again.
    private bool Restore(HotKeyShortcut? previous)
    {
        if (previous is null)
        {
            return false;
        }

        try
        {
            _env.HotKeys.Register(previous, HotKeyAction.ToggleFocusLock, () => Post(FocusLockShortcutPressed));
            return true;
        }
        catch (HotKeyException)
        {
            return false;
        }
    }

    private void SaveFocusLockShortcut(HotKeyShortcut? shortcut)
    {
        // An empty value means "no shortcut", as opposed to an absent key, which means the default.
        _env.Settings.Write(
            SettingsKeys.FocusLockShortcut,
            shortcut is null ? string.Empty : JsonSerializer.Serialize(shortcut, EngineJson.Options));
    }

    /// <summary>The saved shortcut: absent means the default, empty means none, otherwise the stored one (the default if it does not decode).</summary>
    private HotKeyShortcut? StoredFocusLockShortcut()
    {
        if (_env.Settings.Read(SettingsKeys.FocusLockShortcut) is not { } json)
        {
            return HotKeyShortcut.FocusLockDefault;
        }

        if (json.Length == 0)
        {
            return null;
        }

        try
        {
            var shortcut = JsonSerializer.Deserialize<HotKeyShortcut>(json, EngineJson.Options);
            return shortcut is { IsValid: true } ? shortcut : HotKeyShortcut.FocusLockDefault;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return HotKeyShortcut.FocusLockDefault;
        }
    }

    /// <summary>The words for a registration failure.</summary>
    public static string ShortcutMessageFor(HotKeyException error, HotKeyShortcut shortcut) =>
        error.Kind == HotKeyErrorKind.ShortcutInUse
            ? $"{shortcut.DisplayString} is already used by another app. Choose another shortcut in Settings."
            : "The Focus Lock shortcut could not be set up.";
}
