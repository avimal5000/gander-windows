using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Sway.App.Windows;

/// <summary>
/// The plumbing every overlay window shares: clicks go through it, it never takes focus, it stays out of the taskbar and
/// Alt+Tab, and it is placed in physical pixels above everything. It never touches the pointer.
/// </summary>
internal static class OverlayNative
{
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x20;
    private const int WsExToolWindow = 0x80;
    private const int WsExNoActivate = 0x08000000;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private static readonly IntPtr HwndTopmost = new(-1);

    /// <summary>The window options every overlay sets in its constructor.</summary>
    public static void Configure(Window window, string title)
    {
        window.WindowStyle = WindowStyle.None;
        window.AllowsTransparency = true;
        window.Background = System.Windows.Media.Brushes.Transparent;
        window.ShowInTaskbar = false;
        window.ShowActivated = false;
        window.Topmost = true;
        window.Focusable = false;
        window.ResizeMode = ResizeMode.NoResize;
        window.Title = title;
        window.Left = -32000;
        window.Top = -32000;
        window.Width = 100;
        window.Height = 100;
        window.SourceInitialized += (_, _) => MakeClickThrough(window);
    }

    /// <summary>Adds WS_EX_TRANSPARENT, WS_EX_NOACTIVATE and WS_EX_TOOLWINDOW.</summary>
    public static void MakeClickThrough(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        var style = GetWindowLong(handle, GwlExStyle);
        SetWindowLong(handle, GwlExStyle, style | WsExTransparent | WsExNoActivate | WsExToolWindow);
    }

    /// <summary>Puts the window over a frame in physical pixels, topmost, without activating it.</summary>
    public static void Place(Window window, int x, int y, int width, int height)
    {
        if (new WindowInteropHelper(window).Handle is { } handle && handle != IntPtr.Zero)
        {
            SetWindowPos(handle, HwndTopmost, x, y, width, height, SwpNoActivate | SwpShowWindow);
        }
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int value);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
}
