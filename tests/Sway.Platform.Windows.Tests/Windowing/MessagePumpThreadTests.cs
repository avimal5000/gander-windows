using Sway.Platform.Windows.Focus;
using Sway.Platform.Windows.Windowing;

namespace Sway.Platform.Windows.Tests.Windowing;

/// <summary>The real message window and worker thread (Windows only). Audit F16 and F34.</summary>
public class MessagePumpThreadTests
{
    [Fact]
    public void InvokeRunsOnThePumpThreadAndReturnsTheResult()
    {
        using var pump = new MessagePumpThread("test pump", (_, _, _) => false);

        var onPump = pump.Invoke(() => Environment.CurrentManagedThreadId);

        Assert.NotEqual(Environment.CurrentManagedThreadId, onPump);
    }

    [Fact]
    public void InvokeAfterDisposeThrowsInsteadOfWaitingForever()
    {
        var pump = new MessagePumpThread("test pump", (_, _, _) => false);
        pump.Dispose();

        Assert.Throws<ObjectDisposedException>(() => pump.Invoke(() => 1));
    }

    [Fact]
    public async Task CallsRacingADisposeAlwaysEnd()
    {
        for (var round = 0; round < 20; round++)
        {
            var pump = new MessagePumpThread("race pump", (_, _, _) => false);
            var calls = Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
            {
                try
                {
                    pump.Invoke(() => 1);
                }
                catch (ObjectDisposedException)
                {
                }
            })).ToArray();
            pump.Dispose();

            await Task.WhenAll(calls).WaitAsync(TimeSpan.FromSeconds(10)); // would hang before the fix
        }
    }

    [Fact]
    public async Task SerialWorkerRunsWorkInOrderOnOneThread()
    {
        using var worker = new SerialWorker("order");
        var order = new List<int>();
        var tasks = Enumerable.Range(0, 10).Select(i => worker.Run(() =>
        {
            order.Add(i);
            return Environment.CurrentManagedThreadId;
        })).ToArray();

        var threads = await Task.WhenAll(tasks);

        Assert.Equal(Enumerable.Range(0, 10), order);
        Assert.Single(threads.Distinct());
    }
}
