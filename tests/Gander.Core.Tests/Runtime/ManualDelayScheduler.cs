using Gander.Core.Runtime;

namespace Gander.Core.Tests.Runtime;

/// <summary>Delayed work whose time the test moves by hand. Nothing runs until <see cref="Advance"/> reaches it.</summary>
public sealed class ManualDelayScheduler : IDelayScheduler
{
    private sealed record Entry(int Id, double Due, double Delay, Action Work);

    private readonly List<Entry> _entries = new();
    private readonly List<double> _scheduledDelays = new();
    private int _nextId;

    /// <summary>Seconds moved forward so far.</summary>
    public double Now { get; private set; }

    /// <summary>The delay of every action ever scheduled, in order.</summary>
    public IReadOnlyList<double> ScheduledDelays => _scheduledDelays;

    /// <summary>The delays of the actions still waiting, soonest first.</summary>
    public IReadOnlyList<double> PendingDelays =>
        _entries.OrderBy(e => e.Due).ThenBy(e => e.Id).Select(e => e.Delay).ToList();

    public bool HasPending => _entries.Count > 0;

    public IScheduledAction Schedule(double delaySeconds, Action work)
    {
        var id = ++_nextId;
        _entries.Add(new Entry(id, Now + delaySeconds, delaySeconds, work));
        _scheduledDelays.Add(delaySeconds);
        return new Handle(this, id);
    }

    /// <summary>Moves time forward, running whatever falls due on the way in due order.</summary>
    public void Advance(double seconds)
    {
        var target = Now + seconds;
        while (true)
        {
            var next = _entries.Where(e => e.Due <= target + 1e-9).OrderBy(e => e.Due).ThenBy(e => e.Id)
                .FirstOrDefault();
            if (next is null)
            {
                break;
            }

            _entries.Remove(next);
            Now = Math.Max(Now, next.Due);
            next.Work();
        }

        Now = target;
    }

    private sealed class Handle(ManualDelayScheduler owner, int id) : IScheduledAction
    {
        public void Cancel() => owner._entries.RemoveAll(e => e.Id == id);
    }
}
