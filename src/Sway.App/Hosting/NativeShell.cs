using System;
using System.Runtime.InteropServices;

namespace Sway.App.Hosting;

/// <summary>The few Win32 calls the app shell needs: monitor scale and the foreground-change notification.</summary>
internal static class NativeShell
{
    private const uint MonitorDefaultToNearest = 2;
    private const int MdtEffectiveDpi = 0;
    internal const uint EventSystemForeground = 0x0003;
    internal const uint WineventOutOfContext = 0x0000;

    internal delegate void WinEventProc(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(Point point, uint flags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll")]
    internal static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc callback, uint process, uint thread, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnhookWinEvent(IntPtr hook);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyIcon(IntPtr icon);

    /// <summary>The scale of the monitor containing a physical-pixel point, in percent (100, 125...), or null.</summary>
    internal static int? ScalePercentAt(int x, int y)
    {
        try
        {
            var monitor = MonitorFromPoint(new Point { X = x, Y = y }, MonitorDefaultToNearest);
            if (monitor == IntPtr.Zero || GetDpiForMonitor(monitor, MdtEffectiveDpi, out var dpi, out _) != 0 || dpi == 0)
            {
                return null;
            }

            return (int)Math.Round(dpi * 100.0 / 96);
        }
        catch (Exception)
        {
            return null;
        }
    }
}

/// <summary>
/// Tells the app when the foreground window changes (a WinEvent hook: it only reports, it sees no input). The
/// callback arrives on the thread that installed it, which must pump messages (the WPF UI thread does).
/// </summary>
public sealed class ForegroundWatcher : IDisposable
{
    private readonly NativeShell.WinEventProc _callback;
    private IntPtr _hook;

    /// <summary>Installs the hook; <paramref name="changed"/> runs on the calling thread.</summary>
    public ForegroundWatcher(Action changed)
    {
        _callback = (_, _, _, _, _, _, _) => changed();
        _hook = NativeShell.SetWinEventHook(
            NativeShell.EventSystemForeground,
            NativeShell.EventSystemForeground,
            IntPtr.Zero,
            _callback,
            0,
            0,
            NativeShell.WineventOutOfContext);
    }

    /// <summary>True when Windows accepted the hook.</summary>
    public bool IsActive => _hook != IntPtr.Zero;

    /// <inheritdoc />
    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            NativeShell.UnhookWinEvent(_hook);
            _hook = IntPtr.Zero;
        }
    }
}
