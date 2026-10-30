using System.Diagnostics;

namespace Sway.Tools.Spikes;

internal sealed record MonitorInfo(IntPtr Handle, string Device, Native.Rect Bounds, Native.Rect Work, bool Primary, uint Dpi);

internal sealed record WindowInfo(
    IntPtr Handle,
    IntPtr Monitor,
    string ClassName,
    string Process,
    uint ProcessId,
    uint ThreadId,
    Native.Rect Bounds,
    bool? Elevated);

/// <summary>Reads the monitors and the windows on them (spikes S3 and S2 share this).</summary>
internal static class Survey
{
    private static readonly HashSet<string> ShellClasses = new(StringComparer.Ordinal)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Windows.UI.Core.CoreWindow",
        "XamlExplorerHostIslandWindow", "TopLevelWindowForOverflowXamlIsland",
    };

    private static readonly HashSet<string> OwnConsoleClasses = new(StringComparer.Ordinal)
    {
        "ConsoleWindowClass", "CASCADIA_HOSTING_WINDOW_CLASS", "PseudoConsoleWindow",
    };

    /// <summary>The monitors, left to right.</summary>
    internal static List<MonitorInfo> Monitors()
    {
        var found = new List<MonitorInfo>();
        Native.EnumDisplayMonitors(
            IntPtr.Zero,
            IntPtr.Zero,
            (IntPtr monitor, IntPtr hdc, ref Native.Rect rect, IntPtr data) =>
            {
                var info = new Native.MonitorInfoEx { Size = System.Runtime.InteropServices.Marshal.SizeOf<Native.MonitorInfoEx>() };
                if (Native.GetMonitorInfoW(monitor, ref info))
                {
                    var dpi = Native.GetDpiForMonitor(monitor, Native.MdtEffectiveDpi, out var x, out _) == 0 ? x : 96u;
                    found.Add(new MonitorInfo(monitor, info.Device, info.Monitor, info.Work, (info.Flags & 1) != 0, dpi));
                }

                return true;
            },
            IntPtr.Zero);
        return found.OrderBy(monitor => monitor.Bounds.Left).ThenBy(monitor => monitor.Bounds.Top).ToList();
    }

    /// <summary>
    /// The windows Sway would consider (visible, not minimized, not cloaked, not a tool window, not shell chrome, on
    /// the current virtual desktop), topmost first, with a count of what was left out and why.
    /// </summary>
    internal static (List<WindowInfo> Windows, SortedDictionary<string, int> Skipped) Windows()
    {
        var windows = new List<WindowInfo>();
        var skipped = new SortedDictionary<string, int>();
        void Skip(string reason) => skipped[reason] = skipped.GetValueOrDefault(reason) + 1;

        var ownConsole = Native.GetConsoleWindow();
        Native.EnumWindows(
            (window, data) =>
            {
                if (!Native.IsWindowVisible(window))
                {
                    Skip("not visible");
                    return true;
                }

                if (Native.IsIconic(window))
                {
                    Skip("minimized");
                    return true;
                }

                if (Native.DwmGetWindowAttribute(window, Native.DwmwaCloaked, out var cloaked, sizeof(int)) == 0 && cloaked != 0)
                {
                    Skip("cloaked (other virtual desktop or hidden app)");
                    return true;
                }

                var style = (long)Native.GetWindowLongPtr(window, Native.GwlExStyle);
                var hasOwner = Native.GetWindow(window, Native.GwOwner) != IntPtr.Zero;
                var appWindow = (style & Native.WsExAppWindow) != 0;
                if (((style & Native.WsExToolWindow) != 0 || hasOwner) && !appWindow)
                {
                    Skip("tool window or owned popup");
                    return true;
                }

                if ((style & Native.WsExNoActivate) != 0)
                {
                    Skip("never activates");
                    return true;
                }

                var className = Native.ClassName(window);
                if (ShellClasses.Contains(className))
                {
                    Skip("shell chrome");
                    return true;
                }

                if (window == ownConsole || OwnConsoleClasses.Contains(className))
                {
                    Skip("this program's own console");
                    return true;
                }

                if (!Native.GetWindowRect(window, out var bounds) || bounds.Width <= 0 || bounds.Height <= 0)
                {
                    Skip("empty rectangle");
                    return true;
                }

                if (!VirtualDesktops.IsOnCurrent(window))
                {
                    Skip("other virtual desktop");
                    return true;
                }

                var thread = Native.GetWindowThreadProcessId(window, out var processId);
                windows.Add(new WindowInfo(
                    window,
                    Native.MonitorFromWindow(window, Native.MonitorDefaultToNearest),
                    className,
                    ProcessName(processId),
                    processId,
                    thread,
                    bounds,
                    Native.IsElevated(processId)));
                return true;
            },
            IntPtr.Zero);
        return (windows, skipped);
    }

    internal static string ProcessName(uint processId)
    {
        try
        {
            return Process.GetProcessById((int)processId).ProcessName;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return "?";
        }
    }

    internal static string Describe(Native.Rect rect) => $"{rect.Width}x{rect.Height} at ({rect.Left},{rect.Top})";
}
