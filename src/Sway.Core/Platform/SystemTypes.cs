namespace Sway.Core.Platform;

[Flags]
public enum HotKeyModifiers
{
    None = 0,
    Control = 1,
    Alt = 2,
    Shift = 4,
    Win = 8,
}

/// <summary>A global shortcut. <see cref="VirtualKey"/> is a Windows virtual-key code.</summary>
public sealed record HotKeyShortcut(uint VirtualKey, HotKeyModifiers Modifiers, string KeyLabel)
{
    /// <summary>Ctrl+Alt+Shift+L (SPEC D7: Windows reserves Win-key combinations).</summary>
    public static readonly HotKeyShortcut FocusLockDefault = new(0x4C, HotKeyModifiers.Control | HotKeyModifiers.Alt | HotKeyModifiers.Shift, "L");

    /// <summary>Valid when it has at least one of Control, Alt, or Win, and a label.</summary>
    public bool IsValid => (Modifiers & (HotKeyModifiers.Control | HotKeyModifiers.Alt | HotKeyModifiers.Win)) != 0 && KeyLabel.Length > 0;

    public string DisplayString
    {
        get
        {
            var text = string.Empty;
            if (Modifiers.HasFlag(HotKeyModifiers.Control)) { text += "Ctrl+"; }
            if (Modifiers.HasFlag(HotKeyModifiers.Alt)) { text += "Alt+"; }
            if (Modifiers.HasFlag(HotKeyModifiers.Shift)) { text += "Shift+"; }
            if (Modifiers.HasFlag(HotKeyModifiers.Win)) { text += "Win+"; }
            return text + KeyLabel;
        }
    }
}

public enum HotKeyAction
{
    ToggleFocusLock,
}

public enum HotKeyErrorKind
{
    InvalidShortcut,
    ShortcutInUse,
    RegistrationFailed,
}

public sealed class HotKeyException(HotKeyErrorKind kind, string message) : Exception(message)
{
    public HotKeyErrorKind Kind { get; } = kind;
}

/// <summary>Registers global shortcuts (RegisterHotKey: delivers only that one combination, no keyboard hook).</summary>
public interface IHotKeyRegistrar
{
    /// <exception cref="HotKeyException">Invalid, already used by another app, or the system refused.</exception>
    void Register(HotKeyShortcut shortcut, HotKeyAction action, Action handler);

    void Unregister(HotKeyAction action);
}

/// <summary>Things the session does that turn the camera off or back on (lock, sleep, display sleep, user switch).</summary>
public enum SessionEvent
{
    ScreenLocked,
    ScreenUnlocked,
    SystemSleeping,
    SystemWoke,
    DisplaysSleeping,
    DisplaysWoke,
    SessionInactive,
    SessionActive,
}

public interface ISessionEvents
{
    /// <summary>Raised on any thread. Lock and sleep are reported as soon as Windows says so.</summary>
    event Action<SessionEvent>? Occurred;

    /// <summary>True when the screen is locked or the session is not the active one right now (seeds the suspend tracker at launch).</summary>
    bool IsSessionInactiveNow();
}

/// <summary>Small persisted values (JSON file under %AppData%\Sway). Only the user's own choices are saved here.</summary>
public interface ISettingsStore
{
    string? Read(string key);

    void Write(string key, string value);

    void Remove(string key);
}

public enum LoginItemStatus
{
    Enabled,
    Disabled,
    Unavailable,
}

public interface ILoginItem
{
    LoginItemStatus Status { get; }

    void SetEnabled(bool enabled);
}
