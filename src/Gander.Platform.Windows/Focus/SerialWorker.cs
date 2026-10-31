using System.Collections.Concurrent;

namespace Gander.Platform.Windows.Focus;

/// <summary>
/// One dedicated background thread that runs queued work in order. All native window work goes through it, so a window
/// that hangs a call stalls this thread only, never the UI thread, and two calls never overlap.
/// </summary>
internal sealed class SerialWorker : IDisposable
{
    private readonly BlockingCollection<Action> _queue = [];
    private readonly Thread _thread;

    public SerialWorker(string name)
    {
        _thread = new Thread(Loop) { IsBackground = true, Name = name };
        _thread.Start();
    }

    /// <summary>Runs <paramref name="work"/> on the worker thread. Faults in the work fault the returned task.</summary>
    public Task<T> Run<T>(Func<T> work)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            _queue.Add(() =>
            {
                try
                {
                    completion.SetResult(work());
                }
                catch (Exception exception)
                {
                    completion.SetException(exception);
                }
            });
        }
        catch (InvalidOperationException)
        {
            completion.SetException(new ObjectDisposedException(nameof(SerialWorker)));
        }

        return completion.Task;
    }

    public void Dispose()
    {
        _queue.CompleteAdding();
    }

    private void Loop()
    {
        foreach (var work in _queue.GetConsumingEnumerable())
        {
            work();
        }
    }
}
