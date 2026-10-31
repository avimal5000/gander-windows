namespace Gander.Core.Runtime;

/// <summary>What the owner must do after a <see cref="SuspendTracker"/> input.</summary>
public enum SuspendDirectiveKind
{
    /// <summary>Nothing to do.</summary>
    None,

    /// <summary>Tear the session down completely and report suspended with the reason.</summary>
    Suspend,

    /// <summary>Already torn down; only the reported reason changed.</summary>
    ReasonChanged,

    /// <summary>Build the session and report starting.</summary>
    Resume,
}

/// <summary>A directive with the reason it carries (only <c>Suspend</c> and <c>ReasonChanged</c> have one).</summary>
public readonly record struct SuspendDirective(SuspendDirectiveKind Kind, SuspendReason? Reason = null)
{
    public static SuspendDirective None => new(SuspendDirectiveKind.None);

    public static SuspendDirective Resume => new(SuspendDirectiveKind.Resume);

    public static SuspendDirective Suspend(SuspendReason reason) => new(SuspendDirectiveKind.Suspend, reason);

    public static SuspendDirective ReasonChanged(SuspendReason reason) =>
        new(SuspendDirectiveKind.ReasonChanged, reason);
}

/// <summary>A system condition that began or ended, as the session notifications report it.</summary>
public readonly record struct SuspendEvent(bool IsBegin, SuspendReason Reason)
{
    public static SuspendEvent Begin(SuspendReason reason) => new(true, reason);

    public static SuspendEvent End(SuspendReason reason) => new(false, reason);
}

/// <summary>
/// The reason-set state machine behind sleep and lock suspension (privacy guarantee, SPEC D43). It tracks which
/// <see cref="SuspendReason"/>s are active and whether the caller wants capture. It performs no I/O: it only says what
/// the owner should do, so the rules are testable without a camera.
/// <list type="bullet">
/// <item>Capture is torn down when the first reason arrives and rebuilt only when every reason has cleared.</item>
/// <item>Reasons are ordered by arrival; the status reports the earliest one still active.</item>
/// <item>Suspension is sticky: reasons are recorded and cleared whether or not capture is requested, and
/// <see cref="CaptureStopped"/> keeps them. A later <see cref="CaptureRequested"/> therefore answers Suspend while the
/// PC is still asleep or locked, and an <c>End</c> that arrives while capture is stopped clears its reason without
/// resuming anything (it returns None, because nobody asked for capture).</item>
/// </list>
/// Not thread safe; the owner calls it from one thread (lock and unlock delivered immediately, in order).
/// </summary>
public sealed class SuspendTracker
{
    private readonly List<SuspendReason> _reasons = new();

    /// <summary>Active reasons, oldest first, without duplicates.</summary>
    public IReadOnlyList<SuspendReason> Reasons => _reasons;

    /// <summary>True between <see cref="CaptureRequested"/> and <see cref="CaptureStopped"/>.</summary>
    public bool IsCaptureRequested { get; private set; }

    /// <summary>True while at least one reason is active.</summary>
    public bool IsSuspended => _reasons.Count > 0;

    /// <summary>The reason reported while suspended.</summary>
    public SuspendReason? FirstReason => _reasons.Count > 0 ? _reasons[0] : null;

    /// <summary>A copy with the same state, for exploring alternatives.</summary>
    public SuspendTracker Clone()
    {
        var copy = new SuspendTracker { IsCaptureRequested = IsCaptureRequested };
        copy._reasons.AddRange(_reasons);
        return copy;
    }

    /// <summary>
    /// The caller started (or restarted) capture. Returns Suspend if a reason is already active, else Resume, which
    /// here means "build the session now".
    /// </summary>
    public SuspendDirective CaptureRequested()
    {
        IsCaptureRequested = true;
        return _reasons.Count > 0 ? SuspendDirective.Suspend(_reasons[0]) : SuspendDirective.Resume;
    }

    /// <summary>
    /// The caller stopped capture. Active reasons stay: the PC is still asleep or locked, and the next
    /// <see cref="CaptureRequested"/> must see that.
    /// </summary>
    public void CaptureStopped() => IsCaptureRequested = false;

    /// <summary>
    /// A suspend condition began. Recorded even while capture is not requested; the directive is None then.
    /// </summary>
    public SuspendDirective Begin(SuspendReason reason)
    {
        if (_reasons.Contains(reason))
        {
            return SuspendDirective.None;
        }

        var wasSuspended = IsSuspended;
        _reasons.Add(reason);
        return IsCaptureRequested && !wasSuspended ? SuspendDirective.Suspend(reason) : SuspendDirective.None;
    }

    /// <summary>
    /// A suspend condition ended. Cleared even while capture is not requested; the directive is None then.
    /// </summary>
    public SuspendDirective End(SuspendReason reason) => End(new[] { reason });

    /// <summary>
    /// Several conditions ended at the same moment. The directive describes the combined change, so the owner never
    /// sees an intermediate ReasonChanged for a reason that is cleared in the same step.
    /// </summary>
    public SuspendDirective End(IReadOnlyCollection<SuspendReason> ended)
    {
        ArgumentNullException.ThrowIfNull(ended);
        SuspendReason? previousFirst = FirstReason;
        _reasons.RemoveAll(ended.Contains);
        if (!IsCaptureRequested || previousFirst is null)
        {
            return SuspendDirective.None;
        }

        if (FirstReason is not { } newFirst)
        {
            return SuspendDirective.Resume;
        }

        return newFirst == previousFirst ? SuspendDirective.None : SuspendDirective.ReasonChanged(newFirst);
    }

    /// <summary>
    /// Applies a system event, with the self-heal for a missed display wake: when any other condition ends and the
    /// main display is awake (<paramref name="isMainDisplayAsleep"/> says so), DisplaysAsleep is cleared too. A lost
    /// display-on notification must not leave capture suspended. The check runs only when it can matter.
    /// </summary>
    public SuspendDirective Apply(SuspendEvent suspendEvent, Func<bool> isMainDisplayAsleep)
    {
        ArgumentNullException.ThrowIfNull(isMainDisplayAsleep);
        if (suspendEvent.IsBegin)
        {
            return Begin(suspendEvent.Reason);
        }

        var ended = new List<SuspendReason> { suspendEvent.Reason };
        if (suspendEvent.Reason != SuspendReason.DisplaysAsleep
            && _reasons.Contains(SuspendReason.DisplaysAsleep)
            && !isMainDisplayAsleep())
        {
            ended.Add(SuspendReason.DisplaysAsleep);
        }

        return End(ended);
    }
}
