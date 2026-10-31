using Gander.Core;

namespace Gander.Platform.Windows.Windowing;

/// <summary>Why a window is not a focus candidate. <see cref="None"/> means it is eligible.</summary>
public enum WindowRejection
{
    None,
    NotVisible,
    Minimized,
    Cloaked,
    ToolWindowOrOwnedPopup,
    NeverActivates,
    ShellChrome,
    OwnWindow,
    EmptyRectangle,
    OtherVirtualDesktop,
}

/// <summary>
/// The rules of docs/decisions/03: which top-level windows count as "a window the user was using", and which of them is
/// the last-used one on each display. Pure: no Win32 calls.
/// </summary>
public static class WindowEligibility
{
    /// <summary>WS_EX_TOOLWINDOW.</summary>
    public const long ExToolWindow = 0x80;

    /// <summary>WS_EX_APPWINDOW.</summary>
    public const long ExAppWindow = 0x40000;

    /// <summary>WS_EX_NOACTIVATE.</summary>
    public const long ExNoActivate = 0x08000000;

    private static readonly HashSet<string> ShellClasses = new(StringComparer.Ordinal)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Windows.UI.Core.CoreWindow",
        "XamlExplorerHostIslandWindow", "TopLevelWindowForOverflowXamlIsland",
    };

    /// <summary>The desktop, taskbars and shell host windows, which are never focus targets.</summary>
    public static bool IsShellChrome(string className) => ShellClasses.Contains(className);

    /// <summary>True when the window is Gander's own (same process) or shell chrome: never reported as the focused display.</summary>
    public static bool IsOwnOrShell(WindowSnapshot window, int ownProcessId) =>
        window.ProcessId == ownProcessId || IsShellChrome(window.ClassName);

    /// <summary>The first rule the window breaks, in a fixed order, or <see cref="WindowRejection.None"/>.</summary>
    public static WindowRejection Check(WindowSnapshot window, int ownProcessId)
    {
        if (!window.IsVisible)
        {
            return WindowRejection.NotVisible;
        }

        if (window.IsIconic)
        {
            return WindowRejection.Minimized;
        }

        if (window.IsCloaked)
        {
            return WindowRejection.Cloaked;
        }

        var appWindow = (window.ExStyle & ExAppWindow) != 0;
        var toolWindow = (window.ExStyle & ExToolWindow) != 0;
        if ((toolWindow || window.OwnerHandle != 0) && !appWindow)
        {
            return WindowRejection.ToolWindowOrOwnedPopup;
        }

        if ((window.ExStyle & ExNoActivate) != 0)
        {
            return WindowRejection.NeverActivates;
        }

        if (IsShellChrome(window.ClassName))
        {
            return WindowRejection.ShellChrome;
        }

        if (window.ProcessId == ownProcessId)
        {
            return WindowRejection.OwnWindow;
        }

        if (window.Rect.Width <= 0 || window.Rect.Height <= 0)
        {
            return WindowRejection.EmptyRectangle;
        }

        if (!window.IsOnCurrentDesktop)
        {
            return WindowRejection.OtherVirtualDesktop;
        }

        return WindowRejection.None;
    }

    /// <summary>Shorthand for <c>Check(...) == None</c>.</summary>
    public static bool IsEligible(WindowSnapshot window, int ownProcessId) =>
        Check(window, ownProcessId) == WindowRejection.None;

    /// <summary>
    /// The topmost eligible window on each display. <paramref name="topmostFirst"/> must be in z-order, topmost first
    /// (the order EnumWindows returns). A window belongs to the display its monitor maps to; windows whose monitor maps
    /// to no display are ignored. A display with no eligible window is absent from the result.
    /// </summary>
    public static IReadOnlyDictionary<DisplayId, WindowSnapshot> LastUsedByDisplay(
        IEnumerable<WindowSnapshot> topmostFirst,
        int ownProcessId,
        Func<long, DisplayId?> displayOfMonitor)
    {
        var result = new Dictionary<DisplayId, WindowSnapshot>();
        foreach (var window in topmostFirst)
        {
            if (!IsEligible(window, ownProcessId))
            {
                continue;
            }

            if (displayOfMonitor(window.MonitorHandle) is { } display)
            {
                result.TryAdd(display, window);
            }
        }

        return result;
    }
}
