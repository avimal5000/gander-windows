using System;
using System.Collections.Generic;
using System.Linq;
using Gander.Core;
using Gander.Core.Controller;
using Gander.Core.Platform;

namespace Gander.App.Presentation;

/// <summary>One display's row in the Focus Test window.</summary>
/// <param name="Id">The display.</param>
/// <param name="Title">The display's label, with "(focused)" when it holds the focused window.</param>
/// <param name="IsFocused">True when the foreground window is on this display.</param>
/// <param name="WindowText">The last-used window's app name, or "No window". Never a window title.</param>
/// <param name="HasWindow">True when the display has a window.</param>
public sealed record FocusRow(DisplayId Id, string Title, bool IsFocused, string WindowText, bool HasWindow);

/// <summary>One line of the results log.</summary>
public sealed record FocusLogEntry(int Id, DateTimeOffset Time, string Text);

/// <summary>What the Focus Test window is doing about a request.</summary>
public enum FocusActivityKind
{
    /// <summary>Nothing pending.</summary>
    Idle,

    /// <summary>"Focus in 3 s" is counting down.</summary>
    CountingDown,

    /// <summary>A request is running.</summary>
    Focusing,
}

/// <summary>The words and the row layout of the Focus Test window (Mac <c>FocusTestPresentation</c>). Pure.</summary>
public static class FocusTestWording
{
    /// <summary>The window title.</summary>
    public const string Title = "Gander Focus Test";

    /// <summary>The heading inside the window.</summary>
    public const string Heading = "Focus test";

    /// <summary>The displays card heading.</summary>
    public const string DisplaysHeading = "Displays (left to right)";

    /// <summary>Shown with no displays.</summary>
    public const string NoDisplays = "No displays found.";

    /// <summary>Under the rows.</summary>
    public const string Explanation =
        "Focus raises the last-used window on that display and gives it keyboard focus. "
        + "Focus in 3 s waits first, so you can click into another app. The cursor never moves. Window titles are never read.";

    /// <summary>The log card heading.</summary>
    public const string ResultsHeading = "Results";

    /// <summary>Shown before the first result.</summary>
    public const string NoResults = "Results appear here.";

    /// <summary>The immediate button.</summary>
    public const string FocusButton = "Focus";

    /// <summary>The delayed button.</summary>
    public const string DelayedButton = "Focus in 3 s";

    /// <summary>"Cancel (2 s)" while counting down.</summary>
    public static string CancelButton(int seconds) => $"Cancel ({seconds} s)";

    /// <summary>One row per display in layout order.</summary>
    public static IReadOnlyList<FocusRow> Rows(DisplayLayout layout, IReadOnlyDictionary<DisplayId, WindowSummary> windows, DisplayId? focused)
    {
        var labels = DisplayLabeler.Labels(layout);
        return layout.Displays.Select(display =>
        {
            var label = labels.GetValueOrDefault(display.Id, display.Name);
            var isFocused = focused == display.Id;
            windows.TryGetValue(display.Id, out var window);
            return new FocusRow(display.Id, isFocused ? $"{label} (focused)" : label, isFocused, WindowText(window), window is not null);
        }).ToList();
    }

    /// <summary>The app name, or "No window" when the display has none. Windows never gives Gander a title.</summary>
    public static string WindowText(WindowSummary? window) => window is null ? "No window" : window.AppName;

    /// <summary>The log line for a result. Names the app and the display, never a window title.</summary>
    public static string LogText(FocusResult result, Func<DisplayId, string> displayName) => result switch
    {
        FocusResult.Focused focused => $"Focused {focused.Window.AppName} on {displayName(focused.Window.DisplayId)} via {MethodText(focused.Method)} in {focused.Milliseconds} ms",
        FocusResult.NoWindow none => $"No window on {displayName(none.Display)}",
        FocusResult.Failed failed => $"Failed: {failed.Reason}",
        _ => "Failed: unknown result",
    };

    /// <summary>The log line for a request that was cancelled before it ran (the window closed).</summary>
    public const string CancelledText = "Cancelled";

    /// <summary>The wording of a focus method.</summary>
    public static string MethodText(FocusMethod method) => method switch
    {
        FocusMethod.AttachInput => "attached input",
        _ => "plain call",
    };
}

/// <summary>
/// The Focus Test window's state: what is pending, the countdown of "Focus in 3 s", and the results log (Mac
/// <c>AppModel+FocusTest</c>). Time is passed in, so tests move it by hand. One request at a time.
/// </summary>
public sealed class FocusTestState
{
    /// <summary>Seconds between "Focus in 3 s" and the request.</summary>
    public const double DelaySeconds = 3;

    /// <summary>Most results kept in the log.</summary>
    public const int LogLimit = 10;

    private readonly List<FocusLogEntry> _log = [];
    private double _deadline;
    private int _nextId;

    /// <summary>What is pending.</summary>
    public FocusActivityKind Activity { get; private set; }

    /// <summary>The display a countdown or a request is for.</summary>
    public DisplayId? Target { get; private set; }

    /// <summary>Whole seconds left of a countdown, rounded up.</summary>
    public int CountdownSeconds { get; private set; }

    /// <summary>The results, newest first.</summary>
    public IReadOnlyList<FocusLogEntry> Log => _log;

    /// <summary>True when a new request may start.</summary>
    public bool CanRequest => Activity == FocusActivityKind.Idle;

    /// <summary>Starts a countdown to <paramref name="display"/>. Ignored while another request is pending.</summary>
    public bool BeginCountdown(DisplayId display, double now)
    {
        if (!CanRequest)
        {
            return false;
        }

        Activity = FocusActivityKind.CountingDown;
        Target = display;
        _deadline = now + DelaySeconds;
        CountdownSeconds = (int)DelaySeconds;
        return true;
    }

    /// <summary>Cancels a countdown.</summary>
    public void CancelCountdown()
    {
        if (Activity == FocusActivityKind.CountingDown)
        {
            Activity = FocusActivityKind.Idle;
            Target = null;
        }
    }

    /// <summary>Moves a countdown on; returns the display to focus when it has run out (and the state is then Idle until <see cref="BeginFocus"/>).</summary>
    public DisplayId? Tick(double now)
    {
        if (Activity != FocusActivityKind.CountingDown || Target is not { } display)
        {
            return null;
        }

        if (now >= _deadline)
        {
            Activity = FocusActivityKind.Idle;
            Target = null;
            return display;
        }

        CountdownSeconds = (int)Math.Ceiling(_deadline - now);
        return null;
    }

    /// <summary>Marks a request as running. Ignored while another is pending.</summary>
    public bool BeginFocus(DisplayId display)
    {
        if (!CanRequest)
        {
            return false;
        }

        Activity = FocusActivityKind.Focusing;
        Target = display;
        return true;
    }

    /// <summary>Records a result and ends the running request.</summary>
    public void Finish(string text, DateTimeOffset time)
    {
        _log.Insert(0, new FocusLogEntry(_nextId++, time, text));
        if (_log.Count > LogLimit)
        {
            _log.RemoveRange(LogLimit, _log.Count - LogLimit);
        }

        Activity = FocusActivityKind.Idle;
        Target = null;
    }

    /// <summary>Drops a pending countdown, as when the window closes. The log stays until the window is gone.</summary>
    public void Reset()
    {
        Activity = FocusActivityKind.Idle;
        Target = null;
    }
}
