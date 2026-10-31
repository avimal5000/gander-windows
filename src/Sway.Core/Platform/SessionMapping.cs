namespace Sway.Core.Platform;

/// <summary>
/// Turns the numbers in Windows session and power messages into <see cref="SessionEvent"/>. Pure: the Windows adapter
/// passes the raw message fields in, so the mapping is tested on any OS.
/// </summary>
public static class SessionMapping
{
    // WM_WTSSESSION_CHANGE wParam values.
    public const int WtsConsoleConnect = 0x1;
    public const int WtsConsoleDisconnect = 0x2;
    public const int WtsSessionLock = 0x7;
    public const int WtsSessionUnlock = 0x8;

    // WM_POWERBROADCAST wParam values.
    public const int PbtApmSuspend = 0x4;
    public const int PbtApmResumeSuspend = 0x7;
    public const int PbtApmResumeAutomatic = 0x12;

    /// <summary>GUID_CONSOLE_DISPLAY_STATE: 0 off, 1 on, 2 dimmed.</summary>
    public static readonly Guid ConsoleDisplayState = new("6FE69556-704A-47A0-8F24-C28D936FDA47");

    /// <summary>The wParam of WM_WTSSESSION_CHANGE; null for codes Gander does not act on (remote connects, logon, logoff).</summary>
    public static SessionEvent? FromSessionChange(int code) => code switch
    {
        WtsSessionLock => SessionEvent.ScreenLocked,
        WtsSessionUnlock => SessionEvent.ScreenUnlocked,
        WtsConsoleDisconnect => SessionEvent.SessionInactive,
        WtsConsoleConnect => SessionEvent.SessionActive,
        _ => null,
    };

    /// <summary>
    /// The wParam of WM_POWERBROADCAST for the suspend and resume codes; null for the rest (battery, power setting
    /// changes are handled by <see cref="FromPowerSetting"/>). Resume arrives as both ResumeAutomatic and
    /// ResumeSuspend on a user wake, so SystemWoke can repeat; the suspend tracker ignores the repeat.
    /// </summary>
    public static SessionEvent? FromPowerBroadcast(int code) => code switch
    {
        PbtApmSuspend => SessionEvent.SystemSleeping,
        PbtApmResumeAutomatic or PbtApmResumeSuspend => SessionEvent.SystemWoke,
        _ => null,
    };

    /// <summary>
    /// A power setting notification (the POWERBROADCAST_SETTING payload). Only the console display state is known:
    /// a one-byte or four-byte value of 0 (off) or 2 (dimmed) is DisplaysSleeping, 1 (on) is DisplaysWoke.
    /// </summary>
    public static SessionEvent? FromPowerSetting(Guid setting, ReadOnlySpan<byte> data)
    {
        if (setting != ConsoleDisplayState || data.Length == 0)
        {
            return null;
        }

        var value = data.Length >= 4 ? BitConverter.ToInt32(data) : data[0];
        return value switch
        {
            0 or 2 => SessionEvent.DisplaysSleeping,
            1 => SessionEvent.DisplaysWoke,
            _ => null,
        };
    }

    /// <summary>
    /// Whether an input desktop name means the screen is locked. When Windows is locked the input desktop is
    /// "Winlogon" (and a process cannot open it at all, which the adapter reports as a null name).
    /// </summary>
    public static bool IsLockedDesktop(string? inputDesktopName) =>
        inputDesktopName is null || !string.Equals(inputDesktopName, "Default", StringComparison.OrdinalIgnoreCase);

    /// <summary>WTS_CONNECTSTATE_CLASS value of the session: only WTSActive (0) is the active session.</summary>
    public static bool IsActiveConnectState(int connectState) => connectState == 0;
}
