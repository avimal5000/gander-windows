using Gander.Core.Runtime;
using static Gander.Core.Runtime.SuspendDirective;

namespace Gander.Core.Tests.Runtime;

public class SuspendTrackerTests
{
    private static readonly SuspendReason[] AllReasons =
    {
        SuspendReason.SystemSleep, SuspendReason.DisplaysAsleep, SuspendReason.ScreenLocked, SuspendReason.SessionInactive,
    };

    public static TheoryData<SuspendReason> Reasons => new(AllReasons);

    public static TheoryData<SuspendReason> WakeReasons =>
        new(SuspendReason.SystemSleep, SuspendReason.ScreenLocked, SuspendReason.SessionInactive);

    private static SuspendTracker RunningTracker()
    {
        var tracker = new SuspendTracker();
        Assert.Equal(Resume, tracker.CaptureRequested());
        return tracker;
    }

    [Fact]
    public void StartingWithNoReasonsMeansBuildTheSession()
    {
        var tracker = new SuspendTracker();
        Assert.Equal(Resume, tracker.CaptureRequested());
        Assert.True(tracker.IsCaptureRequested);
        Assert.False(tracker.IsSuspended);
    }

    [Fact]
    public void FirstReasonSuspendsAndLastClearedReasonResumes()
    {
        var tracker = RunningTracker();
        Assert.Equal(Suspend(SuspendReason.ScreenLocked), tracker.Begin(SuspendReason.ScreenLocked));
        Assert.True(tracker.IsSuspended);
        Assert.Equal(Resume, tracker.End(SuspendReason.ScreenLocked));
        Assert.False(tracker.IsSuspended);
    }

    [Theory]
    [MemberData(nameof(Reasons))]
    public void EachReasonRoundTrips(SuspendReason reason)
    {
        var tracker = RunningTracker();
        Assert.Equal(Suspend(reason), tracker.Begin(reason));
        Assert.Equal(Resume, tracker.End(reason));
    }

    [Fact]
    public void OverlappingReasonsResumeOnlyWhenAllAreCleared()
    {
        var tracker = RunningTracker();
        Assert.Equal(Suspend(SuspendReason.ScreenLocked), tracker.Begin(SuspendReason.ScreenLocked));
        Assert.Equal(None, tracker.Begin(SuspendReason.DisplaysAsleep));
        Assert.Equal(None, tracker.Begin(SuspendReason.SystemSleep));

        Assert.Equal(None, tracker.End(SuspendReason.SystemSleep));
        Assert.True(tracker.IsSuspended);
        Assert.Equal(None, tracker.End(SuspendReason.DisplaysAsleep));
        Assert.True(tracker.IsSuspended);
        Assert.Equal(Resume, tracker.End(SuspendReason.ScreenLocked));
        Assert.False(tracker.IsSuspended);
    }

    [Fact]
    public void ClearingTheFirstReasonReportsTheNextOne()
    {
        var tracker = RunningTracker();
        tracker.Begin(SuspendReason.ScreenLocked);
        tracker.Begin(SuspendReason.DisplaysAsleep);
        Assert.Equal(SuspendReason.ScreenLocked, tracker.FirstReason);
        Assert.Equal(ReasonChanged(SuspendReason.DisplaysAsleep), tracker.End(SuspendReason.ScreenLocked));
        Assert.Equal(SuspendReason.DisplaysAsleep, tracker.FirstReason);
        Assert.Equal(Resume, tracker.End(SuspendReason.DisplaysAsleep));
    }

    [Fact]
    public void ClearingALaterReasonDoesNotChangeTheReportedOne()
    {
        var tracker = RunningTracker();
        tracker.Begin(SuspendReason.SystemSleep);
        tracker.Begin(SuspendReason.DisplaysAsleep);
        Assert.Equal(None, tracker.End(SuspendReason.DisplaysAsleep));
        Assert.Equal(SuspendReason.SystemSleep, tracker.FirstReason);
    }

    [Fact]
    public void RepeatedBeginIsIgnored()
    {
        var tracker = RunningTracker();
        Assert.Equal(Suspend(SuspendReason.ScreenLocked), tracker.Begin(SuspendReason.ScreenLocked));
        Assert.Equal(None, tracker.Begin(SuspendReason.ScreenLocked));
        Assert.Equal(new[] { SuspendReason.ScreenLocked }, tracker.Reasons);
        Assert.Equal(Resume, tracker.End(SuspendReason.ScreenLocked));
    }

    [Fact]
    public void EndingAReasonThatIsNotActiveDoesNothing()
    {
        var tracker = RunningTracker();
        Assert.Equal(None, tracker.End(SuspendReason.SystemSleep));
        tracker.Begin(SuspendReason.ScreenLocked);
        Assert.Equal(None, tracker.End(SuspendReason.SystemSleep));
        Assert.True(tracker.IsSuspended);
    }

    [Fact]
    public void NoResumeAfterStop()
    {
        var tracker = RunningTracker();
        tracker.Begin(SuspendReason.SystemSleep);
        tracker.CaptureStopped();
        Assert.False(tracker.IsCaptureRequested);
        Assert.True(tracker.IsSuspended);
        Assert.Equal(None, tracker.End(SuspendReason.SystemSleep));
        Assert.False(tracker.IsSuspended);
    }

    [Fact]
    public void ReasonsArrivingAfterStopAreRecordedButChangeNothing()
    {
        var tracker = RunningTracker();
        tracker.CaptureStopped();
        Assert.Equal(None, tracker.Begin(SuspendReason.ScreenLocked));
        Assert.Equal(new[] { SuspendReason.ScreenLocked }, tracker.Reasons);
        Assert.Equal(None, tracker.End(SuspendReason.ScreenLocked));
        Assert.False(tracker.IsSuspended);
    }

    [Fact]
    public void StartingAgainAfterStopStillSeesTheActiveReasons()
    {
        var tracker = RunningTracker();
        tracker.Begin(SuspendReason.ScreenLocked);
        tracker.CaptureStopped();
        Assert.Equal(new[] { SuspendReason.ScreenLocked }, tracker.Reasons);
        Assert.Equal(Suspend(SuspendReason.ScreenLocked), tracker.CaptureRequested());
    }

    [Fact]
    public void StartLockStopStartDoesNotTurnTheCameraOn()
    {
        var tracker = new SuspendTracker();
        Assert.Equal(Resume, tracker.CaptureRequested());
        Assert.Equal(Suspend(SuspendReason.ScreenLocked), tracker.Begin(SuspendReason.ScreenLocked));
        tracker.CaptureStopped();
        Assert.Equal(Suspend(SuspendReason.ScreenLocked), tracker.CaptureRequested());
        Assert.Equal(Resume, tracker.End(SuspendReason.ScreenLocked));
    }

    [Fact]
    public void AReasonEndedWhileStoppedIsClearedAndTheNextStartBuilds()
    {
        var tracker = RunningTracker();
        tracker.Begin(SuspendReason.ScreenLocked);
        tracker.CaptureStopped();
        Assert.Equal(None, tracker.End(SuspendReason.ScreenLocked));
        Assert.Empty(tracker.Reasons);
        Assert.Equal(Resume, tracker.CaptureRequested());
    }

    [Fact]
    public void AReasonBegunWhileStoppedBlocksTheNextStart()
    {
        var tracker = RunningTracker();
        tracker.CaptureStopped();
        Assert.Equal(None, tracker.Begin(SuspendReason.DisplaysAsleep));
        Assert.Equal(Suspend(SuspendReason.DisplaysAsleep), tracker.CaptureRequested());
    }

    [Fact]
    public void ReasonsOverlappingAStopKeepTheirOrder()
    {
        var tracker = RunningTracker();
        tracker.Begin(SuspendReason.SystemSleep);
        tracker.CaptureStopped();
        tracker.Begin(SuspendReason.ScreenLocked);
        Assert.Equal(None, tracker.End(SuspendReason.SystemSleep));
        Assert.Equal(Suspend(SuspendReason.ScreenLocked), tracker.CaptureRequested());
    }

    [Fact]
    public void StartingWhileSuspendedStaysSuspended()
    {
        var tracker = RunningTracker();
        tracker.Begin(SuspendReason.DisplaysAsleep);
        Assert.Equal(Suspend(SuspendReason.DisplaysAsleep), tracker.CaptureRequested());
        Assert.Equal(Resume, tracker.End(SuspendReason.DisplaysAsleep));
    }

    [Fact]
    public void ReasonsBeforeTheFirstStartAreRecorded()
    {
        var tracker = new SuspendTracker();
        Assert.Equal(None, tracker.Begin(SuspendReason.ScreenLocked));
        Assert.True(tracker.IsSuspended);
        Assert.Equal(Suspend(SuspendReason.ScreenLocked), tracker.CaptureRequested());
    }

    [Fact]
    public void AReasonThatEndedBeforeTheFirstStartDoesNotBlockIt()
    {
        var tracker = new SuspendTracker();
        Assert.Equal(None, tracker.Begin(SuspendReason.SystemSleep));
        Assert.Equal(None, tracker.End(SuspendReason.SystemSleep));
        Assert.Equal(Resume, tracker.CaptureRequested());
    }

    [Fact]
    public void SessionInactiveHoldsCaptureOffUntilTheSessionReturns()
    {
        var tracker = RunningTracker();
        Assert.Equal(Suspend(SuspendReason.SessionInactive), tracker.Begin(SuspendReason.SessionInactive));
        Assert.Equal(None, tracker.Begin(SuspendReason.ScreenLocked));
        Assert.Equal(ReasonChanged(SuspendReason.ScreenLocked), tracker.End(SuspendReason.SessionInactive));
        Assert.Equal(Resume, tracker.End(SuspendReason.ScreenLocked));
    }

    [Fact]
    public void SeveralReasonsEndingTogetherGiveOneCombinedDirective()
    {
        var tracker = RunningTracker();
        tracker.Begin(SuspendReason.SystemSleep);
        tracker.Begin(SuspendReason.DisplaysAsleep);
        Assert.Equal(Resume, tracker.End(new[] { SuspendReason.SystemSleep, SuspendReason.DisplaysAsleep }));

        tracker.Begin(SuspendReason.SystemSleep);
        tracker.Begin(SuspendReason.ScreenLocked);
        tracker.Begin(SuspendReason.DisplaysAsleep);
        Assert.Equal(
            ReasonChanged(SuspendReason.ScreenLocked),
            tracker.End(new[] { SuspendReason.SystemSleep, SuspendReason.DisplaysAsleep }));
        Assert.Equal(None, tracker.End(new[] { SuspendReason.SystemSleep, SuspendReason.DisplaysAsleep }));
    }

    [Fact]
    public void RealisticLidCloseSequence()
    {
        var tracker = RunningTracker();
        Assert.Equal(Suspend(SuspendReason.ScreenLocked), tracker.Begin(SuspendReason.ScreenLocked));
        Assert.Equal(None, tracker.Begin(SuspendReason.DisplaysAsleep));
        Assert.Equal(None, tracker.Begin(SuspendReason.SystemSleep));
        Assert.Equal(None, tracker.End(SuspendReason.SystemSleep));
        Assert.Equal(None, tracker.End(SuspendReason.DisplaysAsleep));
        Assert.Equal(Resume, tracker.End(SuspendReason.ScreenLocked));
    }

    // Events and the self-heal for a missed display wake.

    [Fact]
    public void EventsMapToBeginAndEnd()
    {
        var tracker = RunningTracker();
        Assert.Equal(
            Suspend(SuspendReason.ScreenLocked),
            tracker.Apply(SuspendEvent.Begin(SuspendReason.ScreenLocked), () => false));
        Assert.Equal(Resume, tracker.Apply(SuspendEvent.End(SuspendReason.ScreenLocked), () => false));
    }

    [Theory]
    [MemberData(nameof(WakeReasons))]
    public void AWakeClearsAMissedDisplayWakeWhenTheDisplayIsAwake(SuspendReason reason)
    {
        var tracker = RunningTracker();
        tracker.Begin(SuspendReason.DisplaysAsleep);
        tracker.Begin(reason);
        Assert.Equal(Resume, tracker.Apply(SuspendEvent.End(reason), () => false));
        Assert.Empty(tracker.Reasons);
    }

    [Theory]
    [MemberData(nameof(WakeReasons))]
    public void AWakeKeepsDisplaysAsleepWhileTheDisplayIsStillAsleep(SuspendReason reason)
    {
        var tracker = RunningTracker();
        tracker.Begin(reason);
        tracker.Begin(SuspendReason.DisplaysAsleep);
        Assert.Equal(ReasonChanged(SuspendReason.DisplaysAsleep), tracker.Apply(SuspendEvent.End(reason), () => true));
        Assert.Equal(new[] { SuspendReason.DisplaysAsleep }, tracker.Reasons);
    }

    [Fact]
    public void HealingOnlyClearsDisplaysAsleepAndLeavesOtherReasons()
    {
        var tracker = RunningTracker();
        tracker.Begin(SuspendReason.ScreenLocked);
        tracker.Begin(SuspendReason.DisplaysAsleep);
        tracker.Begin(SuspendReason.SessionInactive);
        Assert.Equal(None, tracker.Apply(SuspendEvent.End(SuspendReason.SystemSleep), () => false));
        Assert.Equal(new[] { SuspendReason.ScreenLocked, SuspendReason.SessionInactive }, tracker.Reasons);
    }

    [Fact]
    public void HealingReportsTheNextReasonWhenTheFirstWasTheStaleOne()
    {
        var tracker = RunningTracker();
        tracker.Begin(SuspendReason.DisplaysAsleep);
        tracker.Begin(SuspendReason.ScreenLocked);
        Assert.Equal(
            ReasonChanged(SuspendReason.ScreenLocked),
            tracker.Apply(SuspendEvent.End(SuspendReason.SystemSleep), () => false));
    }

    [Fact]
    public void BeginEventsNeverHeal()
    {
        var tracker = RunningTracker();
        tracker.Begin(SuspendReason.DisplaysAsleep);
        Assert.Equal(None, tracker.Apply(SuspendEvent.Begin(SuspendReason.ScreenLocked), () => false));
        Assert.Equal(new[] { SuspendReason.DisplaysAsleep, SuspendReason.ScreenLocked }, tracker.Reasons);
    }

    [Fact]
    public void HealingWhileStoppedClearsTheReasonAndResumesNothing()
    {
        var tracker = RunningTracker();
        tracker.Begin(SuspendReason.DisplaysAsleep);
        tracker.Begin(SuspendReason.SystemSleep);
        tracker.CaptureStopped();
        Assert.Equal(None, tracker.Apply(SuspendEvent.End(SuspendReason.SystemSleep), () => false));
        Assert.Empty(tracker.Reasons);
        Assert.Equal(Resume, tracker.CaptureRequested());
    }

    [Fact]
    public void TheDisplayCheckRunsOnlyWhenItCanMatter()
    {
        var tracker = RunningTracker();
        var checks = 0;
        bool CountingCheck()
        {
            checks++;
            return false;
        }

        tracker.Begin(SuspendReason.SystemSleep);
        tracker.Apply(SuspendEvent.End(SuspendReason.SystemSleep), CountingCheck);
        tracker.Begin(SuspendReason.DisplaysAsleep);
        tracker.Apply(SuspendEvent.End(SuspendReason.DisplaysAsleep), CountingCheck);
        tracker.Apply(SuspendEvent.Begin(SuspendReason.ScreenLocked), CountingCheck);
        Assert.Equal(0, checks);

        tracker.Begin(SuspendReason.DisplaysAsleep);
        tracker.Apply(SuspendEvent.End(SuspendReason.ScreenLocked), CountingCheck);
        Assert.Equal(1, checks);
    }

    // The privacy invariant.

    private enum StepKind
    {
        Request,
        Stop,
        Event,
    }

    private sealed record Step(StepKind Kind, SuspendEvent Event = default, bool DisplayAsleep = false)
    {
        public static readonly Step[] All = Build();

        private static Step[] Build()
        {
            var steps = new List<Step> { new(StepKind.Request), new(StepKind.Stop) };
            foreach (var reason in AllReasons)
            {
                steps.Add(new Step(StepKind.Event, SuspendEvent.Begin(reason), true));
                steps.Add(new Step(StepKind.Event, SuspendEvent.End(reason), true));
                steps.Add(new Step(StepKind.Event, SuspendEvent.End(reason), false));
            }

            return steps.ToArray();
        }
    }

    private static bool SessionState(SuspendDirective directive, bool wasUp) => directive.Kind switch
    {
        SuspendDirectiveKind.Resume => true,
        SuspendDirectiveKind.Suspend => false,
        _ => wasUp,
    };

    /// <summary>
    /// Whatever the order of starts, stops, and system events, a session is up exactly when capture is requested and no
    /// reason is active. A session never exists while any reason is active (D43).
    /// </summary>
    [Fact]
    public void NoSequenceOfStartsStopsAndEventsKeepsASessionUpWhileSuspended()
    {
        string? failure = null;
        var sequences = 0;

        void Explore(SuspendTracker tracker, bool sessionUp, List<Step> path, int remaining)
        {
            if (failure is not null)
            {
                return;
            }

            sequences++;
            var shouldBeUp = tracker.IsCaptureRequested && !tracker.IsSuspended;
            if (sessionUp != shouldBeUp)
            {
                failure = $"after {path.Count} steps: session up = {sessionUp}, reasons = {string.Join(",", tracker.Reasons)}";
                return;
            }

            if (remaining == 0)
            {
                return;
            }

            foreach (var step in Step.All)
            {
                var next = tracker.Clone();
                var up = sessionUp;
                switch (step.Kind)
                {
                    case StepKind.Request:
                        up = SessionState(next.CaptureRequested(), up);
                        break;
                    case StepKind.Stop:
                        next.CaptureStopped();
                        up = false;
                        break;
                    default:
                        up = SessionState(next.Apply(step.Event, () => step.DisplayAsleep), up);
                        break;
                }

                Explore(next, up, new List<Step>(path) { step }, remaining - 1);
            }
        }

        Explore(new SuspendTracker(), false, new List<Step>(), 4);
        Assert.Null(failure);
        Assert.True(sequences > 1000);
    }
}
