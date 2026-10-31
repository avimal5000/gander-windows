namespace Gander.Core.Controller;

/// <summary>A display treated as focused after an attempt that did not move focus, and what focus was before it.</summary>
public readonly record struct FocusOverride(DisplayId Display, DisplayId? Before);

/// <summary>
/// The bookkeeping of the automatic tracking loop (Mac SPEC F16, D29): which display has keyboard focus as far as the
/// decider should believe, whether a focus request is running, and when a late answer must be dropped. Pure: no I/O and
/// no clock. The focused display is the first known of the request in flight, the override (a display treated as focused
/// after a failed or "no window" attempt), and the last accepted poll. <see cref="Epoch"/> counts everything that can
/// make an in-progress poll or request out of date.
/// </summary>
public sealed class TrackingLoop : IEquatable<TrackingLoop>
{
    private DisplayId? _attemptBefore;

    /// <summary>The last accepted poll result; null when focus is unknown (or Gander itself is in front).</summary>
    public DisplayId? Polled { get; private set; }

    /// <summary>A display Gander treats as focused after an attempt that did not move focus.</summary>
    public FocusOverride? Override { get; private set; }

    /// <summary>The display of the focus request now running.</summary>
    public DisplayId? InFlight { get; private set; }

    /// <summary>Counts attempts, finishes and resets.</summary>
    public int Epoch { get; private set; }

    /// <summary>What the decider should treat as the focused display.</summary>
    public DisplayId? FocusedDisplay => InFlight ?? Override?.Display ?? Polled;

    /// <summary>True while a focus request runs. No other request may start.</summary>
    public bool IsFocusing => InFlight is not null;

    /// <summary>Starts a request for <paramref name="display"/>. Returns false, changing nothing, when one is already running.</summary>
    public bool BeginAttempt(DisplayId display)
    {
        if (InFlight is not null)
        {
            return false;
        }

        _attemptBefore = FocusedDisplay;
        InFlight = display;
        Epoch++;
        return true;
    }

    /// <summary>Ends the running request with its outcome.</summary>
    public void FinishAttempt(DisplayId display, Platform.FocusResult result)
    {
        var before = _attemptBefore;
        InFlight = null;
        _attemptBefore = null;
        Epoch++;
        switch (result)
        {
            case Platform.FocusResult.Focused:
                Polled = display;
                Override = null;
                break;
            default:
                Override = new FocusOverride(display, before);
                break;
        }
    }

    /// <summary>Ends the running request without an outcome (it was cancelled before it acted): focus is unchanged.</summary>
    public void CancelAttempt()
    {
        InFlight = null;
        _attemptBefore = null;
        Epoch++;
    }

    /// <summary>Marks the start of a focused-display poll. Hand the returned epoch to <see cref="PollFinished"/>.</summary>
    public int PollStarted() => Epoch;

    /// <summary>Applies a poll result, unless a request began or ended, or the loop was reset, since the poll started.</summary>
    public void PollFinished(DisplayId? display, int pollEpoch)
    {
        if (pollEpoch != Epoch || InFlight is not null)
        {
            return;
        }

        if (Override is { } current && display == current.Before)
        {
            return;
        }

        Override = null;
        Polled = display;
    }

    /// <summary>
    /// The latest classification of the look. A confident look at a display other than the override's ends the
    /// override: the user has moved on, so a new attempt there is allowed.
    /// </summary>
    public void LookChanged(PoseClassification classification, double enterMargin)
    {
        if (Override is { } current
            && classification.Kind == PoseClassification.ClassKind.Display
            && classification.Best is { } best
            && best != current.Display
            && classification.Margin >= enterMargin)
        {
            Override = null;
        }
    }

    /// <summary>Forgets everything, as after a layout change, a stream restart, or the camera stopping.</summary>
    public void Reset()
    {
        Polled = null;
        Override = null;
        InFlight = null;
        _attemptBefore = null;
        Epoch++;
    }

    /// <summary>A copy with the same state.</summary>
    public TrackingLoop Clone() => new()
    {
        Polled = Polled,
        Override = Override,
        InFlight = InFlight,
        Epoch = Epoch,
        _attemptBefore = _attemptBefore,
    };

    /// <inheritdoc />
    public bool Equals(TrackingLoop? other) =>
        other is not null && Polled == other.Polled && Override == other.Override && InFlight == other.InFlight
        && Epoch == other.Epoch && _attemptBefore == other._attemptBefore;

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as TrackingLoop);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Polled, Override, InFlight, Epoch);

    /// <summary>
    /// The displays focus may move to: the first <paramref name="limit"/> displays of <paramref name="layout"/>, in
    /// layout order, that <paramref name="profile"/> profiles (SPEC D22). Null when that is every display the profile
    /// has, so the decider needs no restriction.
    /// </summary>
    public static IReadOnlySet<DisplayId>? Targets(DisplayLayout layout, CalibrationProfile profile, int limit)
    {
        var profiled = profile.DisplayIds;
        var chosen = layout.Displays.Select(d => d.Id).Where(profiled.Contains).Take(limit).ToHashSet();
        return chosen.SetEquals(profiled) ? null : chosen;
    }
}
