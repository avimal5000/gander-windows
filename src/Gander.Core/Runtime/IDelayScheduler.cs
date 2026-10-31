namespace Gander.Core.Runtime;

/// <summary>A pending one-shot delayed action. Cancelling it after it ran, or twice, does nothing.</summary>
public interface IScheduledAction
{
    /// <summary>Stops the action from running if it has not run yet.</summary>
    void Cancel();
}

/// <summary>
/// Runs a closure once, a little later (display and virtual desktop holds, camera retries, the camera grace). An
/// interface so tests move time by hand instead of sleeping. Mirrors the Mac app's DelayScheduling. The caller that
/// needs UI-thread delivery supplies a scheduler that marshals to that thread.
/// </summary>
public interface IDelayScheduler
{
    /// <summary>Runs <paramref name="work"/> once, <paramref name="delaySeconds"/> from now.</summary>
    IScheduledAction Schedule(double delaySeconds, Action work);
}

/// <summary>
/// The real timer: one <see cref="System.Threading.Timer"/> per scheduled action, so nothing wakes while nothing is
/// pending. The work runs on a thread-pool thread; marshal to the UI thread inside <c>work</c> if needed.
/// </summary>
public sealed class TimerDelayScheduler : IDelayScheduler
{
    public IScheduledAction Schedule(double delaySeconds, Action work)
    {
        ArgumentNullException.ThrowIfNull(work);
        return new TimerAction(TimeSpan.FromSeconds(Math.Max(0, delaySeconds)), work);
    }

    private sealed class TimerAction : IScheduledAction
    {
        private readonly object _gate = new();
        private readonly Timer _timer;
        private bool _cancelled;

        public TimerAction(TimeSpan delay, Action work)
        {
            _timer = new Timer(
                _ =>
                {
                    lock (_gate)
                    {
                        if (_cancelled)
                        {
                            return;
                        }

                        _cancelled = true;
                        _timer?.Dispose();
                    }

                    work();
                },
                null,
                delay,
                Timeout.InfiniteTimeSpan);
        }

        public void Cancel()
        {
            lock (_gate)
            {
                if (_cancelled)
                {
                    return;
                }

                _cancelled = true;
                _timer.Dispose();
            }
        }
    }
}
