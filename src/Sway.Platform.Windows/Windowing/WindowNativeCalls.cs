using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Sway.Core;

namespace Sway.Platform.Windows.Windowing;

/// <summary>The real Win32 implementation of <see cref="IWindowNativeCalls"/>. Compiled on every platform, runs only on Windows.</summary>
internal sealed class WindowNativeCalls : IWindowNativeCalls
{
    private const int GwOwner = 4;
    private const int GwlExStyle = -20;
    private const int DwmwaCloaked = 14;
    private const int MonitorDefaultToNearest = 2;
    private const int SwRestore = 9;
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint TokenQuery = 0x0008;
    private const int TokenElevation = 20;

    private readonly Lazy<bool> _selfElevated = new(() => IsElevated((uint)Environment.ProcessId) == true);

    private delegate bool WindowEnumProc(IntPtr window, IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string Device;
    }

    public bool IsSelfElevated => _selfElevated.Value;

    public IReadOnlyList<WindowSnapshot> EnumerateWindows()
    {
        var windows = new List<WindowSnapshot>();
        EnumWindows(
            (window, _) =>
            {
                if (Snapshot(window) is { } snapshot)
                {
                    windows.Add(snapshot);
                }

                return true;
            },
            IntPtr.Zero);
        return windows;
    }

    public WindowSnapshot? Describe(long handle) => handle == 0 ? null : Snapshot(new IntPtr(handle));

    public long ForegroundWindow() => GetForegroundWindow().ToInt64();

    public bool IsIconic(long handle) => IsIconic(new IntPtr(handle));

    public void Restore(long handle) => ShowWindow(new IntPtr(handle), SwRestore);

    public uint WindowThread(long handle) => handle == 0 ? 0 : GetWindowThreadProcessId(new IntPtr(handle), out _);

    public bool AttachInput(uint otherThread, bool attach) =>
        AttachThreadInput(GetCurrentThreadId(), otherThread, attach);

    public void BringToTop(long handle) => BringWindowToTop(new IntPtr(handle));

    public bool SetForeground(long handle) => SetForegroundWindow(new IntPtr(handle));

    public DisplayRect? MonitorBounds(long monitorHandle)
    {
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        return GetMonitorInfoW(new IntPtr(monitorHandle), ref info) ? ToRect(info.Monitor) : null;
    }

    public void Sleep(int milliseconds) => Thread.Sleep(milliseconds);

    private static WindowSnapshot? Snapshot(IntPtr window)
    {
        var visible = IsWindowVisible(window);
        var iconic = visible && IsIconic(window);
        var cloaked = visible && DwmGetWindowAttribute(window, DwmwaCloaked, out var cloakedValue, sizeof(int)) == 0 && cloakedValue != 0;
        var thread = GetWindowThreadProcessId(window, out var processId);
        if (thread == 0)
        {
            return null;
        }

        var heavy = visible && !iconic && !cloaked;
        var style = (long)GetWindowLongPtr(window, GwlExStyle);
        var owner = GetWindow(window, GwOwner).ToInt64();
        var bounds = GetWindowRect(window, out var rect) ? ToRect(rect) : new DisplayRect(0, 0, 0, 0);
        return new WindowSnapshot(
            window.ToInt64(),
            (int)processId,
            ClassName(window),
            style,
            owner,
            visible,
            iconic,
            cloaked,
            !heavy || VirtualDesktops.IsOnCurrent(window),
            bounds,
            heavy ? MonitorFromWindow(window, MonitorDefaultToNearest).ToInt64() : 0,
            heavy ? ProcessName(processId) : string.Empty,
            heavy ? IsElevated(processId) : null);
    }

    private static DisplayRect ToRect(Rect rect) => new(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);

    private static string ClassName(IntPtr window)
    {
        var text = new StringBuilder(256);
        return GetClassNameW(window, text, text.Capacity) > 0 ? text.ToString() : string.Empty;
    }

    private static string ProcessName(uint processId)
    {
        try
        {
            using var process = Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch (Exception exception) when (exception is not (OutOfMemoryException or AccessViolationException))
        {
            // Includes Win32Exception (access denied, counters): this runs inside the EnumWindows callback, where nothing may escape.
            return "?";
        }
    }

    private static bool? IsElevated(uint processId)
    {
        var process = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (process == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            if (!OpenProcessToken(process, TokenQuery, out var token))
            {
                return null;
            }

            try
            {
                return GetTokenInformation(token, TokenElevation, out var elevated, sizeof(int), out _)
                    ? elevated != 0
                    : null;
            }
            finally
            {
                CloseHandle(token);
            }
        }
        finally
        {
            CloseHandle(process);
        }
    }

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(WindowEnumProc proc, IntPtr data);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr window, int command);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr window, out Rect rect);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(IntPtr window, StringBuilder text, int max);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint attach, uint attachTo, bool doAttach);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr window, int flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfoW(IntPtr monitor, ref MonitorInfo info);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);

    [DllImport("kernel32.dll")]
    private static extern IntPtr OpenProcess(uint access, bool inherit, uint processId);

    [DllImport("advapi32.dll")]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

    [DllImport("advapi32.dll")]
    private static extern bool GetTokenInformation(IntPtr token, int infoClass, out int info, int length, out int returned);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);
}
