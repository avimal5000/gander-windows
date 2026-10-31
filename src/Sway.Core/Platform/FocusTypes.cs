namespace Sway.Core.Platform;

/// <summary>A window Gander may focus. No title: window titles are never read or stored (privacy).</summary>
/// <param name="Handle">The native window handle.</param>
public sealed record WindowSummary(long Handle, int ProcessId, string AppName, DisplayRect Bounds, DisplayId DisplayId);

public enum FocusMethod
{
    /// <summary>Attach to the foreground thread's input queue, bring the window to the top, set it foreground.</summary>
    AttachInput,

    /// <summary>The plain call, used when the target thread already owns the foreground.</summary>
    Plain,
}

/// <summary>The result of a focus request. Always verified by reading the foreground window afterward.</summary>
public abstract record FocusResult
{
    public sealed record Focused(WindowSummary Window, FocusMethod Method, int Milliseconds) : FocusResult;

    /// <summary>The display has no window Gander considers (empty desktop, only minimized or tool windows).</summary>
    public sealed record NoWindow(DisplayId Display) : FocusResult;

    public sealed record Failed(WindowSummary? Window, string Reason) : FocusResult;
}

/// <summary>Finds the last-used window per display and moves keyboard focus. Never moves the cursor (SPEC D1, D9).</summary>
public interface IFocusController
{
    /// <summary>The topmost normal window on each display (SPEC D19); a display with none is absent.</summary>
    Task<IReadOnlyDictionary<DisplayId, WindowSummary>> LastUsedWindowsAsync(DisplayLayout layout);

    /// <summary>The display holding the foreground window, or null when it cannot be told.</summary>
    Task<DisplayId?> FocusedDisplayAsync(DisplayLayout layout);

    /// <summary>
    /// Focuses the last-used window of <paramref name="display"/>. <paramref name="cancellationToken"/> is checked on the
    /// worker right before the first thing that changes focus: a request that was queued behind a slow call and is no
    /// longer wanted (paused, Focus Lock, layout change) ends with <see cref="OperationCanceledException"/> and does nothing.
    /// </summary>
    Task<FocusResult> FocusAsync(DisplayId display, DisplayLayout layout, CancellationToken cancellationToken = default);
}

/// <summary>The monitors, in the same shape the engine uses. Ids are stable across runs for the same monitor.</summary>
public interface IDisplayCatalog
{
    DisplayLayout Current();

    /// <summary>The set, position or size of monitors changed. Raised on any thread; may fire several times for one change.</summary>
    event Action? LayoutChanged;
}
