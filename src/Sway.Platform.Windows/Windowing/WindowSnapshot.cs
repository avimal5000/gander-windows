using Sway.Core;

namespace Sway.Platform.Windows.Windowing;

/// <summary>
/// Everything the eligibility rules need to know about one top-level window, read in one pass so the rules themselves
/// are pure. There is deliberately no title: window titles are never read or stored (privacy).
/// The expensive fields (<see cref="ProcessName"/>, <see cref="IsElevated"/>, <see cref="IsOnCurrentDesktop"/>) are only
/// filled in for windows that are visible, not minimized and not cloaked; for the rest they hold neutral defaults and
/// the window is rejected before they matter.
/// </summary>
/// <param name="Handle">The window handle (HWND).</param>
/// <param name="ProcessId">Owning process id.</param>
/// <param name="ClassName">Window class name.</param>
/// <param name="ExStyle">Extended window style bits (GWL_EXSTYLE).</param>
/// <param name="OwnerHandle">GW_OWNER, 0 when the window has no owner.</param>
/// <param name="IsVisible">IsWindowVisible.</param>
/// <param name="IsIconic">Minimized.</param>
/// <param name="IsCloaked">DWMWA_CLOAKED non-zero (other virtual desktop, hidden UWP app).</param>
/// <param name="IsOnCurrentDesktop">IVirtualDesktopManager says the window is on the current virtual desktop (true if unknown).</param>
/// <param name="Rect">Window rectangle in physical pixels, virtual-screen coordinates.</param>
/// <param name="MonitorHandle">MonitorFromWindow(NEAREST): the monitor holding most of the window.</param>
/// <param name="ProcessName">Executable name without extension, "?" when unreadable.</param>
/// <param name="IsElevated">The owning process runs as administrator; null when unreadable.</param>
public sealed record WindowSnapshot(
    long Handle,
    int ProcessId,
    string ClassName,
    long ExStyle,
    long OwnerHandle,
    bool IsVisible,
    bool IsIconic,
    bool IsCloaked,
    bool IsOnCurrentDesktop,
    DisplayRect Rect,
    long MonitorHandle,
    string ProcessName,
    bool? IsElevated);
