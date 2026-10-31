namespace Gander.Core.Runtime;

/// <summary>How a bounded wait ended.</summary>
public enum BoundedOutcome
{
    /// <summary>The operation finished in time; its value (or fault) is available.</summary>
    Completed,

    /// <summary>The time limit passed first. The operation may still finish later.</summary>
    TimedOut,

    /// <summary>The cancellation token fired first. The operation may still finish later.</summary>
    Cancelled,
}

/// <summary>The result of <see cref="BoundedWait.Open{T}"/>.</summary>
public readonly record struct BoundedResult<T>(BoundedOutcome Outcome, T? Value, Exception? Fault);

/// <summary>
/// Waits for slow native work (a camera driver that stalls) without ever blocking the caller for longer than a limit,
/// so quitting or pausing cannot hang and the camera is not left on by a stuck call (audit F03).
/// </summary>
public static class BoundedWait
{
    /// <summary>Time allowed for a camera to open before the attempt counts as failed and is retried.</summary>
    public static readonly TimeSpan CameraOpenTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Time allowed for a camera reader to stop before the camera is released anyway.</summary>
    public static readonly TimeSpan CameraStopTimeout = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Waits for <paramref name="task"/> for at most <paramref name="timeout"/>. True when it finished (in any state),
    /// false on timeout or cancellation. Never throws the task's own exception.
    /// </summary>
    public static bool TryWait(Task task, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        try
        {
            return task.Wait(timeout, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (AggregateException)
        {
            return true; // faulted or cancelled: it did finish
        }
    }

    /// <summary>
    /// Starts <paramref name="start"/> on the thread pool and waits for it, up to <paramref name="timeout"/> and until
    /// <paramref name="cancellationToken"/> fires. When the wait ends early the operation is abandoned, not aborted:
    /// if it completes later, <paramref name="disposeLate"/> receives its value so a late result cannot keep a camera
    /// open. The caller is blocked for at most the timeout.
    /// </summary>
    public static BoundedResult<T> Open<T>(Func<Task<T>> start, TimeSpan timeout, CancellationToken cancellationToken, Action<T> disposeLate)
    {
        var task = Task.Run(start);
        if (TryWait(task, timeout, cancellationToken))
        {
            return task.Status == TaskStatus.RanToCompletion
                ? new BoundedResult<T>(BoundedOutcome.Completed, task.Result, null)
                : new BoundedResult<T>(BoundedOutcome.Completed, default, task.Exception?.GetBaseException());
        }

        _ = task.ContinueWith(
            finished =>
            {
                if (finished.Status == TaskStatus.RanToCompletion)
                {
                    try
                    {
                        disposeLate(finished.Result);
                    }
                    catch (Exception)
                    {
                        // Nothing more can be done for a result nobody wanted.
                    }
                }
                else
                {
                    _ = finished.Exception; // observed
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return new BoundedResult<T>(cancellationToken.IsCancellationRequested ? BoundedOutcome.Cancelled : BoundedOutcome.TimedOut, default, null);
    }
}
