using Gander.Core.Controller;
using Gander.Core.Platform;
using Gander.Core.Runtime;

namespace Gander.Core.Tests.Controller;

/// <summary>
/// A focus request queued behind a slow call must not act later on stale intent (audit F04). The fake focus controller
/// is held (the slow call) and, like the Windows one, ends the request with OperationCanceledException when its token
/// was cancelled by the time the queued work runs.
/// </summary>
public class FocusCancellationTests
{
    private static readonly DisplayId Right = Fixtures.RightId;

    private static async Task<Harness> SlowRequest()
    {
        var h = Harness.Make(calibrated: true);
        await h.StartTracking();
        h.Drain();
        h.Focus.Result = TrackingTests.FocusedRight();
        h.Focus.HoldFocusRequests();
        TrackingTests.ScriptLook(h, Right, dwellFrom: 99, fireAt: 1.4, ignoresLock: true);
        h.FeedBurst(1.0, 1.4);
        Assert.True(h.Controller.IsSwitching);
        Assert.False(h.Focus.FocusTokens[0].IsCancellationRequested);
        return h;
    }

    private static void ReleaseAndDrain(Harness h)
    {
        h.Focus.Release();
        h.Drain();
    }

    [Fact]
    public async Task PausingTrackingCancelsTheQueuedRequestSoItDoesNothing()
    {
        using var h = await SlowRequest();

        await h.Controller.SetTrackingEnabledAsync(false);
        Assert.True(h.Focus.FocusTokens[0].IsCancellationRequested);
        ReleaseAndDrain(h);

        Assert.Empty(h.Controller.SwitchLog);
        Assert.False(h.Controller.IsSwitching);
    }

    [Fact]
    public async Task FocusLockCancelsTheQueuedRequestAndFreesTheLoopWithoutAFailureEntry()
    {
        using var h = await SlowRequest();

        h.Controller.SetFocusLock(true);
        Assert.True(h.Focus.FocusTokens[0].IsCancellationRequested);
        ReleaseAndDrain(h);

        Assert.Empty(h.Controller.SwitchLog);
        Assert.False(h.Controller.IsSwitching); // not stuck in flight
        Assert.Equal(TrackingHold.FocusLock, h.Controller.TrackingHold);

        h.Controller.SetFocusLock(false);
        h.Focus.Result = TrackingTests.FocusedRight();
        h.Focus.HoldFocusRequests();
        h.Focus.Release();
        h.Feed(2.0);
        Assert.Equal(2, h.Focus.FocusCalls.Count); // a new request is possible afterwards
    }

    [Fact]
    public async Task GanderComingToTheFrontCancelsTheQueuedRequest()
    {
        using var h = await SlowRequest();

        h.Controller.SetGanderInFront(true);
        Assert.True(h.Focus.FocusTokens[0].IsCancellationRequested);
        ReleaseAndDrain(h);

        Assert.Empty(h.Controller.SwitchLog);
        Assert.False(h.Controller.IsSwitching);
    }

    [Fact]
    public async Task ALayoutChangeCancelsTheQueuedRequest()
    {
        using var h = await SlowRequest();

        var rearranged = new DisplayLayout([Fixtures.Left, new DisplayInfo(Right, 12, "Right", new DisplayRect(1920, 0, 1280, 720), false, false)]);
        h.Displays.Change(rearranged);
        h.Drain();
        Assert.True(h.Focus.FocusTokens[0].IsCancellationRequested);
        ReleaseAndDrain(h);

        Assert.Empty(h.Controller.SwitchLog);
    }

    [Fact]
    public async Task ARequestThatIsStillWantedCompletesNormally()
    {
        using var h = await SlowRequest();

        ReleaseAndDrain(h);

        Assert.Single(h.Controller.SwitchLog);
        Assert.False(h.Focus.FocusTokens[0].IsCancellationRequested);
    }

    [Fact]
    public async Task ADesktopChangeStillLetsTheRequestFinish()
    {
        using var h = await SlowRequest();

        h.Controller.ActiveDesktopChanged();
        ReleaseAndDrain(h);

        Assert.Single(h.Controller.SwitchLog);
    }
}
