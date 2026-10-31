using System.Runtime.InteropServices;
using System.Text;

namespace Gander.Platform.Windows;

/// <summary>The Win32 calls behind <see cref="WindowsSessionEvents"/>. Hand-written P/Invoke, no packages.</summary>
internal static class SessionNative
{
    internal const uint WmDestroy = 0x0002;
    internal const uint WmClose = 0x0010;
    internal const uint WmPowerBroadcast = 0x0218;
    internal const uint WmWtsSessionChange = 0x02B1;
    internal const int PbtPowerSettingChange = 0x8013;
    internal const int HwndMessage = -3;
    internal const uint DeviceNotifyWindowHandle = 0;
    internal const uint NotifyForThisSession = 0;
    internal const uint DesktopSwitchDesktop = 0x0100;
    internal const int UoiName = 2;
    internal const int WtsConnectState = 8; // WTS_INFO_CLASS.WTSConnectState
    internal const int WtsCurrentServerHandle = 0;
    internal const uint WtsCurrentSession = 0xFFFFFFFF;

    internal delegate IntPtr WindowProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct WindowClass
    {
        public uint Size;
        public uint Style;
        public IntPtr WindowProc;
        public int ClassExtra;
        public int WindowExtra;
        public IntPtr Instance;
        public IntPtr Icon;
        public IntPtr Cursor;
        public IntPtr Background;
        public string? MenuName;
        public string ClassName;
        public IntPtr SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Message
    {
        public IntPtr Window;
        public uint Id;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern ushort RegisterClassExW(ref WindowClass windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern bool UnregisterClassW(string className, IntPtr instance);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr CreateWindowExW(uint exStyle, string className, string? windowName, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool DestroyWindow(IntPtr window);

    [DllImport("user32.dll")]
    internal static extern IntPtr DefWindowProcW(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern int GetMessageW(out Message message, IntPtr window, uint filterMin, uint filterMax);

    [DllImport("user32.dll")]
    internal static extern bool TranslateMessage(ref Message message);

    [DllImport("user32.dll")]
    internal static extern IntPtr DispatchMessageW(ref Message message);

    [DllImport("user32.dll")]
    internal static extern void PostQuitMessage(int exitCode);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool PostMessageW(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr RegisterPowerSettingNotification(IntPtr recipient, ref Guid setting, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool UnregisterPowerSettingNotification(IntPtr handle);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr RegisterSuspendResumeNotification(IntPtr recipient, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool UnregisterSuspendResumeNotification(IntPtr handle);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    internal static extern bool WTSRegisterSessionNotification(IntPtr window, uint flags);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    internal static extern bool WTSUnRegisterSessionNotification(IntPtr window);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    internal static extern bool WTSQuerySessionInformationW(IntPtr server, uint sessionId, int infoClass, out IntPtr buffer, out uint bytes);

    [DllImport("wtsapi32.dll")]
    internal static extern void WTSFreeMemory(IntPtr memory);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    internal static extern IntPtr GetModuleHandleW(string? moduleName);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint desiredAccess);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool CloseDesktop(IntPtr desktop);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern bool GetUserObjectInformationW(IntPtr handle, int index, StringBuilder buffer, int length, out int needed);

    /// <summary>The connect state of this process's session (WTS_CONNECTSTATE_CLASS), or null when the query fails.</summary>
    internal static int? QueryConnectState()
    {
        if (!WTSQuerySessionInformationW((IntPtr)WtsCurrentServerHandle, WtsCurrentSession, WtsConnectState, out var buffer, out var bytes))
        {
            return null;
        }

        try
        {
            return bytes >= sizeof(int) ? Marshal.ReadInt32(buffer) : null;
        }
        finally
        {
            WTSFreeMemory(buffer);
        }
    }

    /// <summary>
    /// The name of the desktop currently receiving input ("Default" normally, "Winlogon" when locked), or null when it
    /// cannot be opened, which is also what a locked workstation looks like to a normal process.
    /// </summary>
    internal static string? InputDesktopName()
    {
        var desktop = OpenInputDesktop(0, false, DesktopSwitchDesktop);
        if (desktop == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var name = new StringBuilder(256);
            return GetUserObjectInformationW(desktop, UoiName, name, name.Capacity * sizeof(char), out _) ? name.ToString() : null;
        }
        finally
        {
            CloseDesktop(desktop);
        }
    }
}
