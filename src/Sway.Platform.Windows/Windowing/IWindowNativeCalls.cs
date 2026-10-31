using Sway.Core;

namespace Sway.Platform.Windows.Windowing;

/// <summary>
/// The few native window operations the focus controller needs, so the recipe and the rules run against a fake in tests.
/// Every member is called on the controller's single worker thread. Nothing here reads or moves the cursor.
/// </summary>
internal interface IWindowNativeCalls
{
    /// <summary>All top-level windows, topmost first (EnumWindows order).</summary>
    IReadOnlyList<WindowSnapshot> EnumerateWindows();

    /// <summary>One window, or null if it is gone.</summary>
    WindowSnapshot? Describe(long handle);

    /// <summary>The foreground window, 0 when there is none.</summary>
    long ForegroundWindow();

    bool IsIconic(long handle);

    /// <summary>SW_RESTORE.</summary>
    void Restore(long handle);

    /// <summary>The thread that owns the window, 0 when unknown.</summary>
    uint WindowThread(long handle);

    /// <summary>Attach (or detach) the calling thread's input queue to <paramref name="otherThread"/>'s.</summary>
    bool AttachInput(uint otherThread, bool attach);

    void BringToTop(long handle);

    /// <summary>SetForegroundWindow's return value.</summary>
    bool SetForeground(long handle);

    /// <summary>Gander itself runs elevated (then elevated targets are reachable).</summary>
    bool IsSelfElevated { get; }

    /// <summary>The monitor's rectangle in physical pixels, null if the handle is no longer valid.</summary>
    DisplayRect? MonitorBounds(long monitorHandle);

    void Sleep(int milliseconds);
}
