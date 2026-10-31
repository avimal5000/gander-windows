using Gander.Core.Platform;

namespace Gander.Platform.Windows.Hotkeys;

/// <summary>Translations between the Core hotkey types and RegisterHotKey's values. Pure.</summary>
public static class HotKeyMapping
{
    /// <summary>MOD_ALT.</summary>
    public const uint ModAlt = 0x0001;

    /// <summary>MOD_CONTROL.</summary>
    public const uint ModControl = 0x0002;

    /// <summary>MOD_SHIFT.</summary>
    public const uint ModShift = 0x0004;

    /// <summary>MOD_WIN.</summary>
    public const uint ModWin = 0x0008;

    /// <summary>MOD_NOREPEAT: holding the keys down does not repeat the hotkey.</summary>
    public const uint ModNoRepeat = 0x4000;

    /// <summary>ERROR_INVALID_PARAMETER.</summary>
    public const int ErrorInvalidParameter = 87;

    /// <summary>ERROR_HOTKEY_ALREADY_REGISTERED.</summary>
    public const int ErrorHotKeyAlreadyRegistered = 1409;

    /// <summary>The MOD_ flags for <paramref name="modifiers"/> (without MOD_NOREPEAT). The Core and Win32 bit values differ for Control and Alt.</summary>
    public static uint ToNative(HotKeyModifiers modifiers)
    {
        uint native = 0;
        if (modifiers.HasFlag(HotKeyModifiers.Control))
        {
            native |= ModControl;
        }

        if (modifiers.HasFlag(HotKeyModifiers.Alt))
        {
            native |= ModAlt;
        }

        if (modifiers.HasFlag(HotKeyModifiers.Shift))
        {
            native |= ModShift;
        }

        if (modifiers.HasFlag(HotKeyModifiers.Win))
        {
            native |= ModWin;
        }

        return native;
    }

    /// <summary>Maps a failed RegisterHotKey's Win32 error to the exception the caller shows.</summary>
    public static HotKeyException ErrorFor(HotKeyShortcut shortcut, int win32Error) => win32Error switch
    {
        ErrorHotKeyAlreadyRegistered => new HotKeyException(
            HotKeyErrorKind.ShortcutInUse,
            $"{shortcut.DisplayString} is already used by another app."),
        ErrorInvalidParameter => new HotKeyException(
            HotKeyErrorKind.InvalidShortcut,
            $"{shortcut.DisplayString} is not a valid shortcut."),
        _ => new HotKeyException(
            HotKeyErrorKind.RegistrationFailed,
            $"Windows could not register {shortcut.DisplayString} (error {win32Error})."),
    };
}
