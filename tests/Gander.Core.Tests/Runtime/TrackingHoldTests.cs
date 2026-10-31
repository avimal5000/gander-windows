using Gander.Core.Runtime;

namespace Gander.Core.Tests.Runtime;

public class TrackingHoldTests
{
    [Fact]
    public void TheHoldReasonsReadAsInTheContract()
    {
        Assert.Equal("Focus Lock on", TrackingHold.FocusLock.Text());
        Assert.Equal("Gander's menu or window is in front", TrackingHold.GanderInFront.Text());
        Assert.Equal("Switching…", TrackingHold.Switching.Text());
        Assert.Equal("Focus Test running", TrackingHold.FocusTest.Text());
    }

    [Fact]
    public void TheDisplayAndDesktopHoldsHaveTheirOwnWording()
    {
        Assert.Equal("Displays changing…", TrackingHold.DisplaysChanging.Text());
        Assert.Equal("Virtual desktop changing…", TrackingHold.SpaceChanging.Text());
    }

    [Fact]
    public void NothingHeldMeansNoHoldAndNotLocked()
    {
        Assert.Null(TrackingHolds.Resolve(default));
        Assert.False(TrackingHolds.IsLocked(default));
    }

    [Fact]
    public void EachHoldAloneLocksTheDecider()
    {
        var singles = new (TrackingHoldInputs Inputs, TrackingHold Hold)[]
        {
            (new(FocusLocked: true), TrackingHold.FocusLock),
            (new(GanderInFront: true), TrackingHold.GanderInFront),
            (new(FocusTestActive: true), TrackingHold.FocusTest),
            (new(DisplaysChanging: true), TrackingHold.DisplaysChanging),
            (new(SpaceChanging: true), TrackingHold.SpaceChanging),
            (new(Switching: true), TrackingHold.Switching),
        };
        foreach (var (inputs, hold) in singles)
        {
            Assert.Equal(hold, TrackingHolds.Resolve(inputs));
            Assert.True(TrackingHolds.IsLocked(inputs));
        }
    }

    [Fact]
    public void PriorityRunsFromFocusLockDownToSwitching()
    {
        var all = new TrackingHoldInputs(true, true, true, true, true, true);
        Assert.Equal(TrackingHold.FocusLock, TrackingHolds.Resolve(all));
        Assert.Equal(TrackingHold.GanderInFront, TrackingHolds.Resolve(all with { FocusLocked = false }));
        Assert.Equal(
            TrackingHold.FocusTest, TrackingHolds.Resolve(all with { FocusLocked = false, GanderInFront = false }));
        Assert.Equal(
            TrackingHold.DisplaysChanging,
            TrackingHolds.Resolve(new TrackingHoldInputs(DisplaysChanging: true, SpaceChanging: true, Switching: true)));
        Assert.Equal(
            TrackingHold.SpaceChanging,
            TrackingHolds.Resolve(new TrackingHoldInputs(SpaceChanging: true, Switching: true)));
    }

    [Fact]
    public void HoldLogWording()
    {
        Assert.Equal("Display hold started", HoldLog.Started(HoldKind.Displays));
        Assert.Equal("Space hold started", HoldLog.Started(HoldKind.Space));
        Assert.Equal("Display hold ended by cap after 3.00 s", HoldLog.Ended(HoldKind.Displays, HoldEnd.Cap, 3));
        Assert.Equal("Space hold ended by timeout after 0.60 s", HoldLog.Ended(HoldKind.Space, HoldEnd.Timeout, 0.6));
        Assert.Equal("Display hold ended by layout after 0.00 s", HoldLog.Ended(HoldKind.Displays, HoldEnd.Layout, -1));
    }
}

public class ChangeHoldsTests
{
    private sealed class Rig
    {
        public Rig()
        {
            Scheduler = new ManualDelayScheduler();
            Holds = new ChangeHolds(Scheduler, () => Scheduler.Now, Log.Add);
            Holds.DisplayChangeAccepted += () => Resets++;
            Holds.SpaceChangeNotified += () => SpaceNotifications++;
            Holds.HoldEnded += (kind, end) => Ended.Add((kind, end));
        }

        public ManualDelayScheduler Scheduler { get; }

        public ChangeHolds Holds { get; }

        public List<string> Log { get; } = new();

        public List<(HoldKind, HoldEnd)> Ended { get; } = new();

        public int Resets { get; private set; }

        public int SpaceNotifications { get; private set; }
    }

    [Fact]
    public void ConstantsMatchTheContract()
    {
        Assert.Equal(0.75, TrackingHolds.DisplaysChangingTimeout);
        Assert.Equal(3, TrackingHolds.DisplaysChangingCap);
        Assert.Equal(0.6, TrackingHolds.SpaceChangingDuration);
    }

    [Fact]
    public void ADisplayChangeHoldsAndResetsTheLoop()
    {
        var r = new Rig();
        r.Holds.DisplayParametersDidChange();
        Assert.True(r.Holds.DisplaysChanging);
        Assert.Equal(1, r.Resets);
    }

    [Fact]
    public void TheHoldEndsWhenTheNewLayoutIsApplied()
    {
        var r = new Rig();
        r.Holds.DisplayParametersDidChange();

        r.Holds.EndDisplaysChanging(HoldEnd.Layout);

        Assert.False(r.Holds.DisplaysChanging);
        Assert.False(r.Scheduler.HasPending);
        Assert.Equal(new[] { (HoldKind.Displays, HoldEnd.Layout) }, r.Ended);
    }

    [Fact]
    public void TheHoldEndsAfterThreeQuartersOfASecondWhenNoLayoutArrives()
    {
        var r = new Rig();
        r.Holds.DisplayParametersDidChange();
        Assert.Equal(new[] { 0.75 }, r.Scheduler.PendingDelays);

        r.Scheduler.Advance(0.74);
        Assert.True(r.Holds.DisplaysChanging);
        r.Scheduler.Advance(0.01);
        Assert.False(r.Holds.DisplaysChanging);
        Assert.Equal(new[] { (HoldKind.Displays, HoldEnd.Timeout) }, r.Ended);
    }

    [Fact]
    public void AnotherNotificationRestartsTheWait()
    {
        var r = new Rig();
        r.Holds.DisplayParametersDidChange();
        r.Scheduler.Advance(0.5);

        r.Holds.DisplayParametersDidChange();
        r.Scheduler.Advance(0.5);
        Assert.True(r.Holds.DisplaysChanging);

        r.Scheduler.Advance(0.25);
        Assert.False(r.Holds.DisplaysChanging);
    }

    [Fact]
    public void RepeatedNotificationsEveryHalfSecondEndTheHoldThreeSecondsAfterTheFirst()
    {
        var r = new Rig();
        for (var index = 0; index < 6; index++)
        {
            r.Holds.DisplayParametersDidChange();
            Assert.True(r.Holds.DisplaysChanging);
            if (index < 5)
            {
                r.Scheduler.Advance(0.5);
            }
        }

        Assert.Equal(6, r.Resets);
        Assert.Equal(new[] { 0.5 }, r.Scheduler.PendingDelays);

        r.Scheduler.Advance(0.49);
        Assert.True(r.Holds.DisplaysChanging);
        r.Scheduler.Advance(0.01);
        Assert.False(r.Holds.DisplaysChanging);
        Assert.Equal(new[] { (HoldKind.Displays, HoldEnd.Cap) }, r.Ended);
    }

    [Fact]
    public void AfterTheCapTheSameChatterNeitherHoldsNorResets()
    {
        var r = new Rig();
        for (var i = 0; i < 6; i++)
        {
            r.Holds.DisplayParametersDidChange();
            r.Scheduler.Advance(0.5);
        }

        Assert.False(r.Holds.DisplaysChanging);
        var resets = r.Resets;

        for (var i = 0; i < 6; i++)
        {
            r.Holds.DisplayParametersDidChange();
            Assert.False(r.Holds.DisplaysChanging);
            r.Scheduler.Advance(0.5);
        }

        Assert.Equal(resets, r.Resets);
        Assert.False(r.Scheduler.HasPending);
    }

    [Fact]
    public void APauseInTheChatterLetsTheNextNotificationHoldAgain()
    {
        var r = new Rig();
        for (var i = 0; i < 6; i++)
        {
            r.Holds.DisplayParametersDidChange();
            r.Scheduler.Advance(0.5);
        }

        r.Holds.DisplayParametersDidChange();
        Assert.False(r.Holds.DisplaysChanging);

        r.Scheduler.Advance(TrackingHolds.DisplaysChangingTimeout);
        r.Holds.DisplayParametersDidChange();

        Assert.True(r.Holds.DisplaysChanging);
    }

    [Fact]
    public void AHoldThatEndedByItsLayoutOrItsTimeoutDoesNotIgnoreTheNextNotification()
    {
        var r = new Rig();
        r.Holds.DisplayParametersDidChange();
        r.Holds.EndDisplaysChanging(HoldEnd.Layout);
        Assert.False(r.Holds.DisplaysChanging);

        r.Scheduler.Advance(0.1);
        r.Holds.DisplayParametersDidChange();
        Assert.True(r.Holds.DisplaysChanging);

        r.Scheduler.Advance(TrackingHolds.DisplaysChangingTimeout);
        Assert.False(r.Holds.DisplaysChanging);
        r.Scheduler.Advance(0.1);
        r.Holds.DisplayParametersDidChange();
        Assert.True(r.Holds.DisplaysChanging);
    }

    [Fact]
    public void ADesktopChangeHoldsForSixTenthsOfASecond()
    {
        var r = new Rig();
        r.Holds.ActiveSpaceDidChange();
        Assert.True(r.Holds.SpaceChanging);
        Assert.Equal(new[] { 0.6 }, r.Scheduler.PendingDelays);
        Assert.Equal(1, r.SpaceNotifications);
        Assert.Equal(0, r.Resets);

        r.Scheduler.Advance(0.59);
        Assert.True(r.Holds.SpaceChanging);
        r.Scheduler.Advance(0.01);
        Assert.False(r.Holds.SpaceChanging);
        Assert.Equal(new[] { (HoldKind.Space, HoldEnd.Timeout) }, r.Ended);
    }

    [Fact]
    public void AnotherDesktopChangeRestartsTheHold()
    {
        var r = new Rig();
        r.Holds.ActiveSpaceDidChange();
        r.Scheduler.Advance(0.4);
        r.Holds.ActiveSpaceDidChange();
        r.Scheduler.Advance(0.4);
        Assert.True(r.Holds.SpaceChanging);

        r.Scheduler.Advance(0.2);
        Assert.False(r.Holds.SpaceChanging);
    }

    [Fact]
    public void BothHoldsAreLoggedWithTheirDurations()
    {
        var r = new Rig();
        r.Holds.DisplayParametersDidChange();
        r.Scheduler.Advance(0.75);
        r.Holds.ActiveSpaceDidChange();
        r.Scheduler.Advance(0.6);

        Assert.Equal(
            new[]
            {
                "Display hold started",
                "Display hold ended by timeout after 0.75 s",
                "Space hold started",
                "Space hold ended by timeout after 0.60 s",
            },
            r.Log);
    }

    [Fact]
    public void CancelAllDropsBothHoldsAndTheirTimersWithoutEvents()
    {
        var r = new Rig();
        r.Holds.DisplayParametersDidChange();
        r.Holds.ActiveSpaceDidChange();

        r.Holds.CancelAll();

        Assert.False(r.Holds.DisplaysChanging);
        Assert.False(r.Holds.SpaceChanging);
        Assert.False(r.Scheduler.HasPending);
        Assert.Empty(r.Ended);
    }

    [Fact]
    public void ADisplayHoldAndADesktopHoldCombineIntoTheHigherPriorityOne()
    {
        var r = new Rig();
        r.Holds.ActiveSpaceDidChange();
        r.Holds.DisplayParametersDidChange();
        TrackingHold? Current() => TrackingHolds.Resolve(
            new TrackingHoldInputs(DisplaysChanging: r.Holds.DisplaysChanging, SpaceChanging: r.Holds.SpaceChanging));

        Assert.Equal(TrackingHold.DisplaysChanging, Current());
        r.Scheduler.Advance(0.6);
        Assert.Equal(TrackingHold.DisplaysChanging, Current());
        r.Scheduler.Advance(0.15);
        Assert.Null(Current());
    }
}
