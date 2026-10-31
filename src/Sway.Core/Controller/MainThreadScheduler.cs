using Sway.Core.Runtime;

namespace Sway.Core.Controller;

/// <summary>
/// Wraps a scheduler so every delayed action runs on the main thread (posted), is skipped if it was cancelled in the
/// meantime, and is followed by <paramref name="afterRun"/> (the controller publishes its state).
/// </summary>
internal sealed class MainThreadScheduler(IDelayScheduler inner, IMainThread main, Action afterRun) : IDelayScheduler
{
    public IScheduledAction Schedule(double delaySeconds, Action work)
    {
        var handle = new Handle();
        handle.Inner = inner.Schedule(delaySeconds, () => main.Post(() =>
        {
            if (handle.Cancelled)
            {
                return;
            }

            handle.Cancelled = true;
            work();
            afterRun();
        }));
        return handle;
    }

    private sealed class Handle : IScheduledAction
    {
        public IScheduledAction? Inner { get; set; }

        public bool Cancelled { get; set; }

        public void Cancel()
        {
            Cancelled = true;
            Inner?.Cancel();
        }
    }
}

/// <summary>
/// Thrown by a pose source whose start failed, carrying the camera error the controller shows and acts on. Any other
/// exception from <see cref="Platform.IPoseSource.StartAsync"/> is treated as a configuration failure.
/// </summary>
public sealed class CameraStartException(Platform.CameraError error) : Exception(CaptureText.UserMessage(error))
{
    /// <summary>What went wrong.</summary>
    public Platform.CameraError Error { get; } = error;
}
