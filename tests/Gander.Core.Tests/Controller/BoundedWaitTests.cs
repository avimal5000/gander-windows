using Gander.Core.Runtime;

namespace Gander.Core.Tests.Controller;

/// <summary>The bounded waits behind the camera open and stop (audit F03), with fake slow drivers.</summary>
public class BoundedWaitTests
{
    private sealed class Camera : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }

    [Fact]
    public void AnOpenThatFinishesInTimeReturnsItsValue()
    {
        var camera = new Camera();
        var result = BoundedWait.Open(() => Task.FromResult(camera), TimeSpan.FromSeconds(5), CancellationToken.None, c => c.Dispose());

        Assert.Equal(BoundedOutcome.Completed, result.Outcome);
        Assert.Same(camera, result.Value);
        Assert.False(camera.Disposed);
    }

    [Fact]
    public void AFaultedOpenIsReportedNotThrown()
    {
        var result = BoundedWait.Open<Camera>(() => Task.FromException<Camera>(new InvalidOperationException("driver")), TimeSpan.FromSeconds(5), CancellationToken.None, c => c.Dispose());

        Assert.Equal(BoundedOutcome.Completed, result.Outcome);
        Assert.Equal("driver", result.Fault?.Message);
    }

    [Fact]
    public async Task AStalledOpenTimesOutAndALateCameraIsReleased()
    {
        var gate = new TaskCompletionSource<Camera>();
        var camera = new Camera();
        var disposed = new TaskCompletionSource();
        var result = BoundedWait.Open(() => gate.Task, TimeSpan.FromMilliseconds(50), CancellationToken.None, c =>
        {
            c.Dispose();
            disposed.SetResult();
        });

        Assert.Equal(BoundedOutcome.TimedOut, result.Outcome);
        Assert.False(camera.Disposed);

        gate.SetResult(camera); // the driver finally answers: nobody wants the camera any more
        await disposed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(camera.Disposed);
    }

    [Fact]
    public async Task StopDuringOpenReturnsAtOnceAndReleasesTheLateCamera()
    {
        var gate = new TaskCompletionSource<Camera>();
        var camera = new Camera();
        var disposed = new TaskCompletionSource();
        using var stop = new CancellationTokenSource();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var opening = Task.Run(() => BoundedWait.Open(() => gate.Task, TimeSpan.FromSeconds(30), stop.Token, c =>
        {
            c.Dispose();
            disposed.SetResult();
        }));

        await Task.Delay(50);
        stop.Cancel();
        var result = await opening.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(BoundedOutcome.Cancelled, result.Outcome);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5));
        gate.SetResult(camera);
        await disposed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(camera.Disposed);
    }

    [Fact]
    public void AStopThatNeverFinishesGivesUpAfterTheLimit()
    {
        var never = new TaskCompletionSource().Task;
        var watch = System.Diagnostics.Stopwatch.StartNew();

        Assert.False(BoundedWait.TryWait(never, TimeSpan.FromMilliseconds(100)));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void AStopThatFinishedOrFaultedCountsAsDone()
    {
        Assert.True(BoundedWait.TryWait(Task.CompletedTask, TimeSpan.FromSeconds(1)));
        Assert.True(BoundedWait.TryWait(Task.FromException(new InvalidOperationException()), TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void TheCameraLimitsKeepQuitWithinAFewSeconds()
    {
        Assert.True(BoundedWait.CameraStopTimeout <= TimeSpan.FromSeconds(3));
        Assert.True(BoundedWait.CameraOpenTimeout <= TimeSpan.FromSeconds(15));
    }
}
