using Gander.Core.Controller;
using Gander.Core.Platform;

namespace Gander.Core.Tests.Controller;

/// <summary>Port of the Mac TrackingLoopTests ("Tracking loop", 23 tests).</summary>
public class TrackingLoopTests
{
    private static readonly DisplayId Left = Fixtures.LeftId;
    private static readonly DisplayId Right = Fixtures.RightId;
    private static readonly DisplayId Far = Fixtures.FarId;

    private static FocusResult Focused => new FocusResult.Focused(Fixtures.Window(on: Fixtures.Right), FocusMethod.AttachInput, 50);

    private static PoseClassification Look(DisplayId display, double margin = 0.6) => Fixtures.Classification(best: display, margin: margin);

    private static void Poll(TrackingLoop loop, DisplayId? display) => loop.PollFinished(display, loop.PollStarted());

    [Fact]
    public void NothingIsKnownAtFirst()
    {
        var loop = new TrackingLoop();
        Assert.Null(loop.FocusedDisplay);
        Assert.False(loop.IsFocusing);
        Assert.Equal(0, loop.Epoch);
    }

    [Fact]
    public void TheFocusedDisplayPrefersTheRequestInFlightThenTheOverrideThenThePoll()
    {
        var loop = new TrackingLoop();
        Poll(loop, Left);
        Assert.Equal(Left, loop.FocusedDisplay);

        loop.BeginAttempt(Right);
        Assert.Equal(Right, loop.FocusedDisplay);
        Assert.True(loop.IsFocusing);

        loop.FinishAttempt(Right, new FocusResult.NoWindow(Right));
        Assert.Equal(Right, loop.FocusedDisplay);
        Assert.False(loop.IsFocusing);
        Assert.Equal(Left, loop.Polled);
    }

    [Fact]
    public void BeginAttemptRemembersTheFocusAsItWasAndCountsAnEpoch()
    {
        var loop = new TrackingLoop();
        Poll(loop, Left);
        var epoch = loop.Epoch;

        Assert.True(loop.BeginAttempt(Right));
        Assert.Equal(Right, loop.InFlight);
        Assert.Equal(epoch + 1, loop.Epoch);
        loop.FinishAttempt(Right, new FocusResult.Failed(null, "x"));
        Assert.Equal(new FocusOverride(Right, Left), loop.Override);
    }

    [Fact]
    public void OnlyOneAttemptRunsAtATime()
    {
        var loop = new TrackingLoop();
        var first = loop.BeginAttempt(Left);
        var epoch = loop.Epoch;
        var second = loop.BeginAttempt(Right);

        Assert.True(first);
        Assert.False(second);
        Assert.Equal(Left, loop.InFlight);
        Assert.Equal(epoch, loop.Epoch);
    }

    [Fact]
    public void AFocusedResultMakesTheTargetTheFocusAndClearsTheOverride()
    {
        var loop = new TrackingLoop();
        loop.BeginAttempt(Right);
        loop.FinishAttempt(Right, new FocusResult.NoWindow(Right));
        Assert.NotNull(loop.Override);

        loop.BeginAttempt(Left);
        loop.FinishAttempt(Left, Focused);

        Assert.Equal(Left, loop.Polled);
        Assert.Null(loop.Override);
        Assert.Equal(Left, loop.FocusedDisplay);
        Assert.False(loop.IsFocusing);
    }

    [Fact]
    public void ANoWindowOrFailedResultTreatsTheTargetAsFocusedUntilFocusMoves()
    {
        foreach (var result in new FocusResult[] { new FocusResult.NoWindow(Right), new FocusResult.Failed(null, "Focus did not move") })
        {
            var loop = new TrackingLoop();
            Poll(loop, Left);
            loop.BeginAttempt(Right);
            loop.FinishAttempt(Right, result);

            Assert.Equal(Right, loop.FocusedDisplay);
            Assert.Equal(Left, loop.Polled);
        }
    }

    [Fact]
    public void FinishingCountsAnotherEpoch()
    {
        var loop = new TrackingLoop();
        loop.BeginAttempt(Right);
        var epoch = loop.Epoch;
        loop.FinishAttempt(Right, Focused);
        Assert.Equal(epoch + 1, loop.Epoch);
    }

    [Fact]
    public void APollResultBecomesTheFocusWithoutAnOverride()
    {
        var loop = new TrackingLoop();
        Poll(loop, Right);
        Assert.Equal(Right, loop.Polled);
        Poll(loop, null);
        Assert.Null(loop.Polled);
    }

    [Fact]
    public void APollThatStartedBeforeAnAttemptIsIgnoredAfterIt()
    {
        var loop = new TrackingLoop();
        Poll(loop, Left);
        var stale = loop.PollStarted();

        loop.BeginAttempt(Right);
        loop.FinishAttempt(Right, Focused);
        loop.PollFinished(Left, stale);

        Assert.Equal(Right, loop.Polled);
    }

    [Fact]
    public void APollThatFinishesWhileAnAttemptIsInFlightIsIgnored()
    {
        var loop = new TrackingLoop();
        Poll(loop, Left);
        loop.BeginAttempt(Right);
        var during = loop.PollStarted();

        loop.PollFinished(Far, during);

        Assert.Equal(Left, loop.Polled);
    }

    [Fact]
    public void APollThatStartedBeforeAResetIsIgnored()
    {
        var loop = new TrackingLoop();
        var stale = loop.PollStarted();
        loop.Reset();
        loop.PollFinished(Right, stale);
        Assert.Null(loop.Polled);
    }

    [Fact]
    public void AnOverrideSurvivesAPollThatStillSaysWhatFocusWasBefore()
    {
        var loop = new TrackingLoop();
        Poll(loop, Left);
        loop.BeginAttempt(Right);
        loop.FinishAttempt(Right, new FocusResult.NoWindow(Right));

        Poll(loop, Left);

        Assert.Equal(new FocusOverride(Right, Left), loop.Override);
        Assert.Equal(Right, loop.FocusedDisplay);
    }

    [Fact]
    public void AnOverrideSurvivesAnUnknownPollWhenFocusWasUnknownBefore()
    {
        var loop = new TrackingLoop();
        loop.BeginAttempt(Right);
        loop.FinishAttempt(Right, new FocusResult.NoWindow(Right));

        Poll(loop, null);

        Assert.Equal(Right, loop.FocusedDisplay);
    }

    [Fact]
    public void APollThatSaysSomethingElseClearsTheOverride()
    {
        var loop = new TrackingLoop();
        Poll(loop, Left);
        loop.BeginAttempt(Right);
        loop.FinishAttempt(Right, new FocusResult.NoWindow(Right));

        Poll(loop, Far);

        Assert.Null(loop.Override);
        Assert.Equal(Far, loop.Polled);
        Assert.Equal(Far, loop.FocusedDisplay);
    }

    [Fact]
    public void AConfidentLookAtAnotherDisplayClearsTheOverride()
    {
        var loop = new TrackingLoop();
        Poll(loop, Left);
        loop.BeginAttempt(Right);
        loop.FinishAttempt(Right, new FocusResult.NoWindow(Right));

        loop.LookChanged(Look(Left), 0.3);

        Assert.Null(loop.Override);
        Assert.Equal(Left, loop.FocusedDisplay);
    }

    [Fact]
    public void TheOverridesOwnDisplayAWeakLookAndOtherKindsOfLookKeepTheOverride()
    {
        var loop = new TrackingLoop();
        Poll(loop, Left);
        loop.BeginAttempt(Right);
        loop.FinishAttempt(Right, new FocusResult.NoWindow(Right));
        var kept = loop.Clone();

        loop.LookChanged(Look(Right), 0.3);
        Assert.Equal(kept, loop);
        loop.LookChanged(Look(Left, 0.29), 0.3);
        Assert.Equal(kept, loop);
        loop.LookChanged(Fixtures.Classification(PoseClassification.ClassKind.Elsewhere), 0.3);
        Assert.Equal(kept, loop);
        loop.LookChanged(PoseClassification.NoFace, 0.3);
        Assert.Equal(kept, loop);
    }

    [Fact]
    public void AMarginExactlyAtTheEnterMarginCounts()
    {
        var loop = new TrackingLoop();
        loop.BeginAttempt(Right);
        loop.FinishAttempt(Right, new FocusResult.NoWindow(Right));

        loop.LookChanged(Look(Left, 0.3), 0.3);

        Assert.Null(loop.Override);
    }

    [Fact]
    public void ALookChangeWithoutAnOverrideChangesNothing()
    {
        var loop = new TrackingLoop();
        Poll(loop, Left);
        var before = loop.Clone();
        loop.LookChanged(Look(Right), 0.3);
        Assert.Equal(before, loop);
    }

    [Fact]
    public void ResetClearsEverythingAndCountsAnEpoch()
    {
        var loop = new TrackingLoop();
        Poll(loop, Left);
        loop.BeginAttempt(Right);
        var epoch = loop.Epoch;

        loop.Reset();

        Assert.Null(loop.Polled);
        Assert.Null(loop.Override);
        Assert.Null(loop.InFlight);
        Assert.Null(loop.FocusedDisplay);
        Assert.Equal(epoch + 1, loop.Epoch);
        Assert.True(loop.BeginAttempt(Left));
    }

    [Fact]
    public void AllCalibratedDisplaysAreTargetsUpToTheLimit()
    {
        Assert.Null(TrackingLoop.Targets(Fixtures.TwoDisplays, Fixtures.Profile(Fixtures.TwoDisplays), 3));
        Assert.Null(TrackingLoop.Targets(Fixtures.ThreeDisplays, Fixtures.Profile(Fixtures.ThreeDisplays), 3));
    }

    [Fact]
    public void AFourthDisplayIsNotATarget()
    {
        var targets = TrackingLoop.Targets(Fixtures.FourDisplays, Fixtures.Profile(Fixtures.FourDisplays), 3);
        Assert.True(new HashSet<DisplayId> { Fixtures.LeftId, Fixtures.RightId, Fixtures.FarId }.SetEquals(targets!));
    }

    [Fact]
    public void TargetsFollowLayoutOrderAndSkipUncalibratedDisplays()
    {
        // The profile has Right, Far and Extra but not Left: the first 2 calibrated displays in layout order.
        var profile = Fixtures.Profile(new DisplayLayout([Fixtures.Right, Fixtures.Far, Fixtures.Extra]));
        var targets = TrackingLoop.Targets(Fixtures.FourDisplays, profile, 2);
        Assert.True(new HashSet<DisplayId> { Fixtures.RightId, Fixtures.FarId }.SetEquals(targets!));
    }
}
