using Gander.Core.Runtime;

namespace Gander.Core.Platform;

/// <summary>What the capture loop must do next, as decided by <see cref="CaptureSupervisor"/>.</summary>
public enum SupervisorAction
{
    None,

    /// <summary>No frame for <see cref="CaptureSupervisor.StallTimeout"/>: report Interrupted and release the camera.</summary>
    ReportInterrupted,

    /// <summary>The wait is over: open the camera again.</summary>
    Rebuild,
}

/// <summary>
/// The frame-stall watchdog and retry timing of a camera capture, with no clock and no I/O: the caller passes the
/// time in seconds (any monotonic origin). Frames flowing resets <see cref="RetryBackoff"/>; a stall waits
/// 10, 30, then 60 s before rebuilding, and a failed open waits 1, 2, 5, then 10 s. Not thread safe.
/// </summary>
public sealed class CaptureSupervisor
{
    /// <summary>Seconds without a frame while running before the capture counts as interrupted.</summary>
    public const double StallTimeout = 3;

    private readonly RetryBackoff _backoff;
    private double _lastFrame;
    private double _rebuildAt;

    public CaptureSupervisor(RetryBackoff? backoff = null) => _backoff = backoff ?? new RetryBackoff();

    public enum Phase
    {
        /// <summary>Not capturing (before the first open, or after a wait was started by a failure).</summary>
        Idle,
        Running,

        /// <summary>Waiting for the rebuild time.</summary>
        Waiting,
    }

    public Phase State { get; private set; } = Phase.Idle;

    /// <summary>The time the next rebuild is due; meaningful while <see cref="State"/> is Waiting.</summary>
    public double RebuildAt => _rebuildAt;

    /// <summary>The camera opened at <paramref name="now"/>; the stall clock starts.</summary>
    public void Opened(double now)
    {
        State = Phase.Running;
        _lastFrame = now;
    }

    /// <summary>Opening failed at <paramref name="now"/>. Returns the delay before the next try (1, 2, 5, then 10 s).</summary>
    public double OpenFailed(double now)
    {
        var delay = _backoff.NextOpenFailureDelay();
        State = Phase.Waiting;
        _rebuildAt = now + delay;
        return delay;
    }

    /// <summary>A frame arrived at <paramref name="now"/>, processed or not. Frames flowing reset the retry delays.</summary>
    public void FrameArrived(double now)
    {
        _lastFrame = now;
        _backoff.FramesFlowed();
    }

    /// <summary>
    /// The capture itself reported a failure (the camera may have been unplugged) while running: treat it as a stall
    /// right now. Returns <see cref="SupervisorAction.ReportInterrupted"/>, or None when not running.
    /// </summary>
    public SupervisorAction Interrupt(double now)
    {
        if (State != Phase.Running)
        {
            return SupervisorAction.None;
        }

        State = Phase.Waiting;
        _rebuildAt = now + _backoff.NextStallRebuildDelay();
        return SupervisorAction.ReportInterrupted;
    }

    /// <summary>Called regularly (a few times a second). Returns what to do now.</summary>
    public SupervisorAction Tick(double now)
    {
        switch (State)
        {
            case Phase.Running when now - _lastFrame >= StallTimeout:
                State = Phase.Waiting;
                _rebuildAt = now + _backoff.NextStallRebuildDelay();
                return SupervisorAction.ReportInterrupted;
            case Phase.Waiting when now >= _rebuildAt:
                State = Phase.Idle;
                return SupervisorAction.Rebuild;
            default:
                return SupervisorAction.None;
        }
    }
}
