using System.Globalization;

namespace Sway.Core.Runtime;

/// <summary>Why automatic switching is held right now (SPEC D28, D44). The decider is told it is locked while any applies.</summary>
public enum TrackingHold
{
    /// <summary>The user turned Focus Lock on.</summary>
    FocusLock,

    /// <summary>Gander's tray menu or one of its windows is in front.</summary>
    SwayInFront,

    /// <summary>A Focus Test countdown or request is running.</summary>
    FocusTest,

    /// <summary>The displays are being plugged, unplugged, or rearranged and the new layout is not known yet. Not shown in the menu.</summary>
    DisplaysChanging,

    /// <summary>A virtual desktop just changed, so the idea of the front window is still settling. Not shown in the menu.</summary>
    SpaceChanging,

    /// <summary>Gander's own focus request is in flight.</summary>
    Switching,
}

/// <summary>The state the hold rules read.</summary>
public readonly record struct TrackingHoldInputs(
    bool FocusLocked = false,
    bool SwayInFront = false,
    bool FocusTestActive = false,
    bool DisplaysChanging = false,
    bool SpaceChanging = false,
    bool Switching = false);

/// <summary>Priority, wording, and combination of <see cref="TrackingHold"/>s.</summary>
public static class TrackingHolds
{
    /// <summary>Longest a display hold lasts from its first notification, however many follow, in seconds.</summary>
    public const double DisplaysChangingCap = 3;

    /// <summary>Longest a display change holds when no new layout arrives, in seconds.</summary>
    public const double DisplaysChangingTimeout = 0.75;

    /// <summary>How long a virtual desktop change holds, in seconds.</summary>
    public const double SpaceChangingDuration = 0.6;

    /// <summary>
    /// The first hold that applies, in priority order: Focus Lock, Gander in front, Focus Test, displays changing,
    /// virtual desktop changing, Switching. Null when none applies.
    /// </summary>
    public static TrackingHold? Resolve(TrackingHoldInputs inputs)
    {
        if (inputs.FocusLocked)
        {
            return TrackingHold.FocusLock;
        }

        if (inputs.SwayInFront)
        {
            return TrackingHold.SwayInFront;
        }

        if (inputs.FocusTestActive)
        {
            return TrackingHold.FocusTest;
        }

        if (inputs.DisplaysChanging)
        {
            return TrackingHold.DisplaysChanging;
        }

        if (inputs.SpaceChanging)
        {
            return TrackingHold.SpaceChanging;
        }

        return inputs.Switching ? TrackingHold.Switching : null;
    }

    /// <summary>True when the decider must be told it is locked: any hold applies.</summary>
    public static bool IsLocked(TrackingHoldInputs inputs) => Resolve(inputs) is not null;

    /// <summary>The wording in the Pose Debug window. Windows wording: the tray menu or window, virtual desktop.</summary>
    public static string Text(this TrackingHold hold) => hold switch
    {
        TrackingHold.FocusLock => "Focus Lock on",
        TrackingHold.SwayInFront => "Gander's menu or window is in front",
        TrackingHold.FocusTest => "Focus Test running",
        TrackingHold.DisplaysChanging => "Displays changing…",
        TrackingHold.SpaceChanging => "Virtual desktop changing…",
        TrackingHold.Switching => "Switching…",
        _ => throw new ArgumentOutOfRangeException(nameof(hold)),
    };
}

/// <summary>Which timed hold.</summary>
public enum HoldKind
{
    Displays,
    Space,
}

/// <summary>What ended a hold.</summary>
public enum HoldEnd
{
    /// <summary>The new layout was applied.</summary>
    Layout,

    /// <summary>The wait for a layout ran out.</summary>
    Timeout,

    /// <summary>A display hold reached its cap: notifications kept coming without a layout.</summary>
    Cap,
}

/// <summary>What the log says about a display or virtual desktop hold. Never carries a window title.</summary>
public static class HoldLog
{
    public static string Started(HoldKind kind) => $"{Name(kind)} hold started";

    public static string Ended(HoldKind kind, HoldEnd end, double seconds) =>
        $"{Name(kind)} hold ended by {EndName(end)} after "
        + $"{Math.Max(0, seconds).ToString("F2", CultureInfo.InvariantCulture)} s";

    private static string Name(HoldKind kind) => kind == HoldKind.Displays ? "Display" : "Space";

    private static string EndName(HoldEnd end) => end switch
    {
        HoldEnd.Layout => "layout",
        HoldEnd.Timeout => "timeout",
        _ => "cap",
    };
}

/// <summary>
/// The timing of the display and virtual desktop holds (SPEC D44, CONTRACT 6.5.5 item 1). Switching waits while the
/// displays or the desktops change. A display change holds until the new layout is applied
/// (<see cref="EndDisplaysChanging"/> with Layout) or <see cref="TrackingHolds.DisplaysChangingTimeout"/> passes; a
/// further notification restarts the wait, but never past <see cref="TrackingHolds.DisplaysChangingCap"/> seconds from
/// the first one. After the cap, the same chatter is ignored until it pauses for the timeout. A desktop change holds
/// for <see cref="TrackingHolds.SpaceChangingDuration"/>, restarted by another. Timers come from the injected
/// scheduler and the clock is injected (monotonic seconds), so tests never sleep. Not thread safe.
/// </summary>
public sealed class ChangeHolds
{
    private readonly IDelayScheduler _scheduler;
    private readonly Func<double> _clock;
    private readonly Action<string>? _log;

    private IScheduledAction? _displaysTimer;
    private IScheduledAction? _spaceTimer;
    private double? _lastDisplayNotification;
    private double? _displaysHoldStart;
    private double? _spaceHoldStart;
    private bool _displaysHoldCapped;

    public ChangeHolds(IDelayScheduler scheduler, Func<double> clock, Action<string>? log = null)
    {
        _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _log = log;
    }

    /// <summary>True while a display hold applies.</summary>
    public bool DisplaysChanging { get; private set; }

    /// <summary>True while a virtual desktop hold applies.</summary>
    public bool SpaceChanging { get; private set; }

    /// <summary>
    /// A display notification was accepted (not ignored as post-cap chatter): the owner resets the tracking loop, which
    /// also drops a switch in flight.
    /// </summary>
    public event Action? DisplayChangeAccepted;

    /// <summary>A virtual desktop notification arrived: the owner drops a focus poll that may have seen the old desktop.</summary>
    public event Action? SpaceChangeNotified;

    /// <summary>A hold ended: the owner looks at where focus is once.</summary>
    public event Action<HoldKind, HoldEnd>? HoldEnded;

    /// <summary>The display configuration changed (plugged, unplugged, rearranged).</summary>
    public void DisplayParametersDidChange()
    {
        var now = _clock();
        var previous = _lastDisplayNotification;
        _lastDisplayNotification = now;
        if (_displaysHoldCapped)
        {
            if (previous is { } last && now - last < TrackingHolds.DisplaysChangingTimeout)
            {
                return;
            }

            _displaysHoldCapped = false;
        }

        DisplayChangeAccepted?.Invoke();
        if (!DisplaysChanging)
        {
            DisplaysChanging = true;
            _displaysHoldStart = now;
            _log?.Invoke(HoldLog.Started(HoldKind.Displays));
        }

        var remaining = TrackingHolds.DisplaysChangingCap - (now - (_displaysHoldStart ?? now));
        var reachesCap = remaining < TrackingHolds.DisplaysChangingTimeout;
        _displaysTimer?.Cancel();
        _displaysTimer = _scheduler.Schedule(
            reachesCap ? Math.Max(0, remaining) : TrackingHolds.DisplaysChangingTimeout,
            () => EndDisplaysChanging(reachesCap ? HoldEnd.Cap : HoldEnd.Timeout));
    }

    /// <summary>The new layout is known, or the wait ran out: switching may resume.</summary>
    public void EndDisplaysChanging(HoldEnd end)
    {
        _displaysTimer?.Cancel();
        _displaysTimer = null;
        if (!DisplaysChanging)
        {
            return;
        }

        DisplaysChanging = false;
        if (end == HoldEnd.Cap)
        {
            _displaysHoldCapped = true;
        }

        var now = _clock();
        var lasted = now - (_displaysHoldStart ?? now);
        _displaysHoldStart = null;
        _log?.Invoke(HoldLog.Ended(HoldKind.Displays, end, lasted));
        HoldEnded?.Invoke(HoldKind.Displays, end);
    }

    /// <summary>The active virtual desktop changed.</summary>
    public void ActiveSpaceDidChange()
    {
        SpaceChangeNotified?.Invoke();
        if (!SpaceChanging)
        {
            SpaceChanging = true;
            _spaceHoldStart = _clock();
            _log?.Invoke(HoldLog.Started(HoldKind.Space));
        }

        _spaceTimer?.Cancel();
        _spaceTimer = _scheduler.Schedule(TrackingHolds.SpaceChangingDuration, EndSpaceChanging);
    }

    /// <summary>The virtual desktop has settled: switching may resume.</summary>
    public void EndSpaceChanging()
    {
        _spaceTimer?.Cancel();
        _spaceTimer = null;
        if (!SpaceChanging)
        {
            return;
        }

        SpaceChanging = false;
        var now = _clock();
        var lasted = now - (_spaceHoldStart ?? now);
        _spaceHoldStart = null;
        _log?.Invoke(HoldLog.Ended(HoldKind.Space, HoldEnd.Timeout, lasted));
        HoldEnded?.Invoke(HoldKind.Space, HoldEnd.Timeout);
    }

    /// <summary>Drops both holds and their timers (quitting). Raises no events.</summary>
    public void CancelAll()
    {
        _displaysTimer?.Cancel();
        _displaysTimer = null;
        _spaceTimer?.Cancel();
        _spaceTimer = null;
        _displaysHoldStart = null;
        _spaceHoldStart = null;
        _displaysHoldCapped = false;
        DisplaysChanging = false;
        SpaceChanging = false;
    }
}
