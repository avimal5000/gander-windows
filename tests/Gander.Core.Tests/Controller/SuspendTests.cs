using Gander.Core.Controller;
using Gander.Core.Platform;
using Gander.Core.Runtime;

namespace Gander.Core.Tests.Controller;

/// <summary>
/// Sleep, lock, displays off and session switching through ISessionEvents and the Core SuspendTracker. The Mac suspends in
/// its capture layer; here the controller does it (see CONTRACT section W5), so these have no Swift counterpart.
/// </summary>
public class SuspendTests
{
    private static async Task<Harness> Running()
    {
        var h = Harness.Make(autoRun: true, calibrated: true);
        await h.SetTracking(true);
        return h;
    }

    [Theory]
    [InlineData(SessionEvent.ScreenLocked, SessionEvent.ScreenUnlocked, SuspendReason.ScreenLocked)]
    [InlineData(SessionEvent.SystemSleeping, SessionEvent.SystemWoke, SuspendReason.SystemSleep)]
    [InlineData(SessionEvent.DisplaysSleeping, SessionEvent.DisplaysWoke, SuspendReason.DisplaysAsleep)]
    [InlineData(SessionEvent.SessionInactive, SessionEvent.SessionActive, SuspendReason.SessionInactive)]
    public async Task AnEventSuspendsTheCameraAndItsEndResumes(SessionEvent begin, SessionEvent end, SuspendReason reason)
    {
        using var h = await Running();

        h.RaiseSession(begin);

        Assert.Equal(new CaptureStatus.Suspended(reason), h.Controller.Status);
        Assert.Equal(TrackingStatus.Suspended(reason), h.Controller.TrackingStatus);
        Assert.True(h.Controller.TrackingEnabled);
        Assert.Equal(1, h.Source.StopCount);
        Assert.False(h.Source.IsStreaming);

        h.RaiseSession(end);

        Assert.Equal(new CaptureStatus.Running(Fixtures.BuiltInCamera), h.Controller.Status);
        Assert.Equal(2, h.Source.StartCount);
        Assert.True(h.Source.IsStreaming);
    }

    [Fact]
    public async Task ResumeWaitsForEverySuspendReasonToEnd()
    {
        using var h = await Running();

        h.RaiseSession(SessionEvent.ScreenLocked);
        h.RaiseSession(SessionEvent.SystemSleeping);
        h.RaiseSession(SessionEvent.ScreenUnlocked);

        Assert.IsType<CaptureStatus.Suspended>(h.Controller.Status);
        Assert.Equal(1, h.Source.StartCount);

        h.RaiseSession(SessionEvent.SystemWoke);

        Assert.IsType<CaptureStatus.Running>(h.Controller.Status);
        Assert.Equal(2, h.Source.StartCount);
    }

    [Fact]
    public async Task AMissedDisplayWakeHealsWhenAnotherConditionEndsAndTheMainDisplayIsAwake()
    {
        using var h = await Running();

        h.RaiseSession(SessionEvent.DisplaysSleeping);
        h.RaiseSession(SessionEvent.ScreenLocked);
        h.RaiseSession(SessionEvent.ScreenUnlocked); // IsMainDisplayAsleep is unset: the display is awake.

        Assert.IsType<CaptureStatus.Running>(h.Controller.Status);
    }

    [Fact]
    public async Task SwitchingIsPausedWhileSuspendedAndResumesAfter()
    {
        using var h = await Running();
        h.Focus.Result = TrackingTests.FocusedRight();
        h.Engine.Script.Rule = (sample, _) => Fixtures.Decision(
            switchTo: Fixtures.RightId,
            phase: SwitchPhase.CoolingDown(sample.Timestamp + 0.75),
            classification: Fixtures.Classification(best: Fixtures.RightId));

        h.RaiseSession(SessionEvent.ScreenLocked);
        h.Feed(1.0);
        Assert.Empty(h.Focus.FocusCalls);

        h.RaiseSession(SessionEvent.ScreenUnlocked);
        h.Feed(1.1);
        Assert.Equal([Fixtures.RightId], h.Focus.FocusCalls);
    }

    [Fact]
    public async Task ASessionThatIsInactiveAtLaunchStartsSuspendedAndWaitsForTheSessionToReturn()
    {
        using var h = Harness.Make(autoRun: true, calibrated: true, trackingSaved: true, sessionInactive: true);

        Assert.True(h.Controller.TrackingEnabled);
        Assert.Equal(0, h.Source.StartCount);
        Assert.Equal(new CaptureStatus.Suspended(SuspendReason.SessionInactive), h.Controller.Status);

        h.RaiseSession(SessionEvent.SessionActive);
        await Task.CompletedTask;

        Assert.Equal(1, h.Source.StartCount);
        Assert.IsType<CaptureStatus.Running>(h.Controller.Status);
    }

    [Fact]
    public async Task TurningTrackingOffWhileSuspendedForgetsTheSession()
    {
        using var h = await Running();
        h.RaiseSession(SessionEvent.ScreenLocked);

        await h.SetTracking(false);
        h.RaiseSession(SessionEvent.ScreenUnlocked);

        Assert.False(h.Controller.IsCameraActive);
        Assert.Equal(1, h.Source.StartCount);
        Assert.Equal(TrackingStatus.Paused, h.Controller.TrackingStatus);
    }

    [Fact]
    public async Task TurningTrackingOnWhileLockedWaitsForTheUnlock()
    {
        using var h = Harness.Make(autoRun: true, calibrated: true);
        h.RaiseSession(SessionEvent.ScreenLocked);

        await h.SetTracking(true);
        Assert.Equal(0, h.Source.StartCount);
        Assert.Equal(new CaptureStatus.Suspended(SuspendReason.ScreenLocked), h.Controller.Status);

        h.RaiseSession(SessionEvent.ScreenUnlocked);
        Assert.Equal(1, h.Source.StartCount);
        Assert.IsType<CaptureStatus.Running>(h.Controller.Status);
    }
}
