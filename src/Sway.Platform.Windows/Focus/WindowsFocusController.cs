using Sway.Core;
using Sway.Core.Platform;
using Sway.Platform.Windows.Windowing;

namespace Sway.Platform.Windows.Focus;

/// <summary>
/// Finds the last-used window per display and moves keyboard focus with the recipe verified in spike S2
/// (docs/decisions/02-focus.md): restore if minimized, attach to the foreground thread's input queue, BringWindowToTop,
/// SetForegroundWindow, detach in a finally, then poll up to 600 ms for the foreground window to be the target.
/// All native work runs on one dedicated worker thread; the returned tasks complete on the thread pool. Never reads or
/// moves the cursor, never synthesizes input, never reads a window title. Methods do not throw: a failure is a
/// <see cref="FocusResult.Failed"/> (or, for the queries, an empty answer).
/// </summary>
public sealed class WindowsFocusController : IFocusController, IDisposable
{
    /// <summary>How long a focus request waits for the foreground window to change.</summary>
    public const int VerifyTimeoutMilliseconds = 600;

    /// <summary>Poll step while waiting.</summary>
    public const int VerifyStepMilliseconds = 50;

    private readonly IWindowNativeCalls _native;
    private readonly int _ownProcessId;
    private readonly SerialWorker _worker;

    /// <summary>The real controller.</summary>
    public WindowsFocusController()
        : this(new WindowNativeCalls(), Environment.ProcessId, new SerialWorker("Sway focus"))
    {
    }

    internal WindowsFocusController(IWindowNativeCalls native, int ownProcessId, SerialWorker worker)
    {
        _native = native;
        _ownProcessId = ownProcessId;
        _worker = worker;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<DisplayId, WindowSummary>> LastUsedWindowsAsync(DisplayLayout layout)
    {
        try
        {
            return await _worker.Run(() => LastUsed(layout)).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new Dictionary<DisplayId, WindowSummary>();
        }
    }

    /// <inheritdoc />
    public async Task<DisplayId?> FocusedDisplayAsync(DisplayLayout layout)
    {
        try
        {
            return await _worker.Run(() => FocusedDisplay(layout)).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<FocusResult> FocusAsync(DisplayId display, DisplayLayout layout)
    {
        try
        {
            return await _worker.Run(() => Focus(display, layout)).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new FocusResult.Failed(null, $"Focus failed unexpectedly: {exception.GetType().Name}.");
        }
    }

    /// <summary>Stops the worker thread after the queued work finishes.</summary>
    public void Dispose() => _worker.Dispose();

    private Dictionary<DisplayId, WindowSummary> LastUsed(DisplayLayout layout)
    {
        var displayOf = DisplayMapper.Create(layout, _native.MonitorBounds);
        var found = WindowEligibility.LastUsedByDisplay(_native.EnumerateWindows(), _ownProcessId, displayOf);
        return found.ToDictionary(pair => pair.Key, pair => Summary(pair.Value, pair.Key));
    }

    private DisplayId? FocusedDisplay(DisplayLayout layout)
    {
        var foreground = _native.ForegroundWindow();
        if (foreground == 0 || _native.Describe(foreground) is not { } window)
        {
            return null;
        }

        if (WindowEligibility.IsOwnOrShell(window, _ownProcessId) || window.MonitorHandle == 0)
        {
            return null;
        }

        return DisplayMapper.Create(layout, _native.MonitorBounds)(window.MonitorHandle);
    }

    private FocusResult Focus(DisplayId display, DisplayLayout layout)
    {
        var displayOf = DisplayMapper.Create(layout, _native.MonitorBounds);
        var candidates = WindowEligibility.LastUsedByDisplay(_native.EnumerateWindows(), _ownProcessId, displayOf);
        if (!candidates.TryGetValue(display, out var target))
        {
            return new FocusResult.NoWindow(display);
        }

        var summary = Summary(target, display);
        if (_native.ForegroundWindow() == target.Handle)
        {
            return new FocusResult.Focused(summary, FocusMethod.Plain, 0);
        }

        // The display already has the front window (another window of the same display, or the target's own dialog):
        // nothing to do. Moving focus between two windows of one display would only fight the user's choice.
        var front = _native.ForegroundWindow();
        if (front != 0 && _native.Describe(front) is { } frontWindow
            && !WindowEligibility.IsOwnOrShell(frontWindow, _ownProcessId)
            && frontWindow.MonitorHandle != 0
            && displayOf(frontWindow.MonitorHandle) == display)
        {
            return new FocusResult.Focused(Summary(frontWindow, display), FocusMethod.Plain, 0);
        }

        if (_native.IsIconic(target.Handle))
        {
            _native.Restore(target.Handle);
        }

        var foregroundThread = _native.WindowThread(_native.ForegroundWindow());
        var targetThread = _native.WindowThread(target.Handle);
        var attached = foregroundThread != 0 && foregroundThread != targetThread && _native.AttachInput(foregroundThread, true);
        try
        {
            _native.BringToTop(target.Handle);
            _native.SetForeground(target.Handle);
        }
        finally
        {
            if (attached)
            {
                _native.AttachInput(foregroundThread, false);
            }
        }

        // The target must be in front on two reads one step apart. A window that is in front for an instant and then
        // loses focus again (the previous app reclaiming it) is not a switch.
        var method = attached ? FocusMethod.AttachInput : FocusMethod.Plain;
        var firstSeen = -1;
        for (var waited = 0; waited <= VerifyTimeoutMilliseconds; waited += VerifyStepMilliseconds)
        {
            if (_native.ForegroundWindow() == target.Handle)
            {
                if (firstSeen >= 0)
                {
                    return new FocusResult.Focused(summary, method, firstSeen);
                }

                firstSeen = waited;
            }
            else
            {
                firstSeen = -1;
            }

            if (waited < VerifyTimeoutMilliseconds)
            {
                _native.Sleep(VerifyStepMilliseconds);
            }
        }

        if (firstSeen >= 0 && _native.ForegroundWindow() == target.Handle)
        {
            return new FocusResult.Focused(summary, method, firstSeen);
        }

        return new FocusResult.Failed(summary, FailureReason(target));
    }

    private string FailureReason(WindowSnapshot target)
    {
        if (target.IsElevated == true && !_native.IsSelfElevated)
        {
            return $"{target.ProcessName} runs as administrator and Sway does not, so Windows will not let Sway give it focus.";
        }

        return $"Windows did not bring {target.ProcessName} to the front within {VerifyTimeoutMilliseconds} ms.";
    }

    private static WindowSummary Summary(WindowSnapshot window, DisplayId display) =>
        new(window.Handle, window.ProcessId, window.ProcessName, window.Rect, display);
}
