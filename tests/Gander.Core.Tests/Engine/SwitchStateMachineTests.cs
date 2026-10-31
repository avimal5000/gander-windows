namespace Gander.Core.Tests;

/// <summary>
/// Port of the Mac SwitchStateMachineTests: one test per rule of the switch state machine. The hand profile and the
/// 0.125 s timestamp grid keep every comparison exact. Settings are the defaults with dwell 0.5: enterMargin 0.30,
/// stayMargin 0.15, maxGap 0.25, cooldown 0.75. Streams are continuous (no skipped frames).
/// </summary>
public class SwitchStateMachineTests
{
    private static readonly DisplayId A = HandProfile.A;
    private static readonly DisplayId B = HandProfile.B;
    private static readonly DisplayId C = HandProfile.C;
    private const double Epsilon = SwitchStateMachine.TimeTolerance;

    private static SwitchPhase Dwelling(DisplayId display, double progress) => SwitchPhase.Dwelling(display, progress);

    private static void AllNoSwitch(IEnumerable<SwitchDecision> decisions) => Assert.All(decisions, d => Assert.Null(d.SwitchTo));

    // Dwell

    [Fact]
    public void FiresAtExactlyTheFirstSampleWhereTheDwellHasElapsed()
    {
        var harness = new DeciderHarness(); // focus on B, looking at A
        var decisions = harness.Run(0, 0.5, HandProfile.YawA);
        Assert.Equal(5, decisions.Count);
        double[] progress = [0.0, 0.25, 0.5, 0.75];
        for (var i = 0; i < 4; i++)
        {
            Assert.Null(decisions[i].SwitchTo);
            Assert.Equal(Dwelling(A, progress[i]), decisions[i].Phase);
            Assert.Equal(SwitchHoldReason.Dwelling, decisions[i].Reason);
        }

        var fired = decisions[4]; // t - start == dwell
        Assert.Equal(A, fired.SwitchTo);
        Assert.Equal(SwitchPhase.CoolingDown(1.25), fired.Phase);
        Assert.Null(fired.Reason);
        Assert.Equal(A, fired.Classification.Best);
    }

    [Fact]
    public void FiresOnTheBoundaryFrameWhenTheDifferenceOfLargeTimestampsRoundsBelowTheDwell()
    {
        // Host-uptime timestamps near 3e5 s resolve about 6e-11 s, so two stamps exactly 0.4 s apart can differ by
        // 0.39999999996 in doubles. That frame must still complete the 0.4 s dwell, not fire one frame late.
        const double start = 310_297.743;
        var boundary = Math.BitDecrement(start + 0.4);
        Assert.True(boundary - start < 0.4);
        Assert.True(0.4 - (boundary - start) < 1e-9);

        var harness = new DeciderHarness(SwitchSettings.Default);
        var stamps = Enumerable.Range(0, 6).Select(i => start + (i * 0.4 / 6)).ToList(); // 6 frames, 0.0667 s apart
        stamps.Add(boundary);
        var decisions = stamps.Select(t => harness.Feed(t, HandProfile.YawA)).ToList();
        Assert.Equal(Dwelling(A, 0), decisions[0].Phase);
        AllNoSwitch(decisions.Take(6));
        var fired = decisions[6];
        Assert.Equal(A, fired.SwitchTo);
        Assert.Equal(SwitchPhase.CoolingDown(boundary + SwitchSettings.Default.Cooldown), fired.Phase);
    }

    [Fact]
    public void TheDwellClockStartsAtTheFirstQualifyingSampleNotAtZero()
    {
        var harness = new DeciderHarness();
        harness.Run(0, 9.875, HandProfile.YawB); // looking at the focused display
        var decisions = harness.Run(10, 10.5, HandProfile.YawA);
        Assert.Equal(Dwelling(A, 0), decisions[0].Phase);
        AllNoSwitch(decisions.Take(4));
        Assert.Equal(A, decisions[4].SwitchTo);
    }

    [Fact]
    public void AGlanceShorterThanTheDwellNeverFires()
    {
        var harness = new DeciderHarness();
        var glance = harness.Run(0, 0.375, HandProfile.YawA); // one frame short of the dwell
        AllNoSwitch(glance);
        var back = harness.Feed(0.5, HandProfile.YawB);
        Assert.Null(back.SwitchTo);
        Assert.Equal(SwitchPhase.Idle, back.Phase);
        Assert.Equal(SwitchHoldReason.AlreadyFocused, back.Reason);

        // A second look starts from scratch rather than resuming the first.
        var again = harness.Run(0.625, 1.125, HandProfile.YawA);
        Assert.Equal(Dwelling(A, 0), again[0].Phase);
        Assert.Equal(Dwelling(A, 0.75), again[3].Phase);
        AllNoSwitch(again.Take(4));
        Assert.Equal(A, again[4].SwitchTo);
    }

    // Gaps

    [Fact]
    public void AGapNoLongerThanMaxGapSurvivesAndTheSwitchWaitsForASupportingSample()
    {
        var harness = new DeciderHarness();
        harness.Run(0, 0.125, HandProfile.YawA); // last support at 0.125
        var first = harness.Feed(0.25, null); // no face, gap 0.125
        Assert.Null(first.SwitchTo);
        Assert.Equal(Dwelling(A, 0.5), first.Phase);
        Assert.Equal(SwitchHoldReason.Dwelling, first.Reason);
        var second = harness.Feed(0.375, HandProfile.YawA); // support again after exactly maxGap: not "greater than"
        Assert.Null(second.SwitchTo);
        Assert.Equal(Dwelling(A, 0.75), second.Phase); // the original clock kept running
        Assert.Equal(A, harness.Feed(0.5, HandProfile.YawA).SwitchTo);
    }

    [Fact]
    public void ASupportingSampleAfterAGapLongerThanMaxGapStartsAFreshDwell()
    {
        var harness = new DeciderHarness();
        harness.Run(0, 0.125, HandProfile.YawA); // last support at 0.125
        harness.Feed(0.25, null);
        var stillAlive = harness.Feed(0.375, null); // gap exactly maxGap
        Assert.Equal(Dwelling(A, 0.75), stillAlive.Phase);
        // Support returns after 0.375 s without it: too late, so this is a new dwell, not a switch.
        var late = harness.Feed(0.5, HandProfile.YawA);
        Assert.Null(late.SwitchTo);
        Assert.Equal(Dwelling(A, 0), late.Phase);
        AllNoSwitch(harness.Run(0.625, 0.875, HandProfile.YawA));
        Assert.Equal(A, harness.Feed(1.0, HandProfile.YawA).SwitchTo);
    }

    [Fact]
    public void NoSwitchFiresOnAnUnsupportedSampleEvenAfterTheDwellHasElapsed()
    {
        var harness = new DeciderHarness();
        harness.Run(0, 0.375, HandProfile.YawA); // supports through t = 0.375
        var unsupported = harness.Feed(0.5, null); // dwell elapsed, gap 0.125
        Assert.Null(unsupported.SwitchTo);
        Assert.Equal(Dwelling(A, 1), unsupported.Phase);
        Assert.Equal(A, harness.Feed(0.625, HandProfile.YawA).SwitchTo);
    }

    public enum GapSample
    {
        NoFace,
        Elsewhere,
        Uncertain,
        LowConfidence,
    }

    public static IEnumerable<object[]> GapSamples() => Enum.GetValues<GapSample>().Select(g => new object[] { g });

    private static SwitchHoldReason ExpectedReason(GapSample gap) => gap switch
    {
        GapSample.NoFace or GapSample.LowConfidence => SwitchHoldReason.NoFace,
        GapSample.Elsewhere => SwitchHoldReason.Elsewhere,
        _ => SwitchHoldReason.Uncertain,
    };

    private static SwitchDecision FeedGap(GapSample gap, DeciderHarness harness, double t) => gap switch
    {
        GapSample.NoFace => harness.Feed(t, null),
        GapSample.Elsewhere => harness.Feed(t, HandProfile.YawElsewhere),
        GapSample.Uncertain => harness.Feed(t, HandProfile.YawVeryWeakA), // margin 0.1, below stay
        _ => harness.Feed(t, HandProfile.YawA, confidence: 0.2),
    };

    [Theory]
    [MemberData(nameof(GapSamples))]
    public void AGapLongerThanMaxGapCancelsTheDwell(GapSample gap)
    {
        var harness = new DeciderHarness();
        harness.Run(0, 0.125, HandProfile.YawA); // last supporting sample at 0.125
        Assert.Equal(Dwelling(A, 0.5), FeedGap(gap, harness, 0.25).Phase);
        Assert.Equal(Dwelling(A, 0.75), FeedGap(gap, harness, 0.375).Phase);
        var cancelled = FeedGap(gap, harness, 0.5); // 0.375 s without support > maxGap
        Assert.Null(cancelled.SwitchTo);
        Assert.Equal(SwitchPhase.Idle, cancelled.Phase);
        Assert.Equal(ExpectedReason(gap), cancelled.Reason);

        // Looking at A again starts a new dwell; it must not fire on the old clock.
        var restarted = harness.Run(0.625, 1.125, HandProfile.YawA);
        Assert.Equal(Dwelling(A, 0), restarted[0].Phase);
        AllNoSwitch(restarted.Take(4));
        Assert.Equal(A, restarted[4].SwitchTo);
    }

    [Fact]
    public void AHoleInTheStreamStartsAFreshDwellInsteadOfFiring()
    {
        var harness = new DeciderHarness();
        var before = harness.Run(0, 0.25, HandProfile.YawA); // dwell running, last support at 0.25
        Assert.Equal(Dwelling(A, 0.5), before[^1].Phase);

        // No samples for 3 s (a camera stall), then a supporting sample. The old dwell's clock is far past the
        // dwell, but it must not fire: this sample starts a fresh dwell.
        var resumed = harness.Run(3.25, 3.75, HandProfile.YawA);
        Assert.Null(resumed[0].SwitchTo);
        Assert.Equal(Dwelling(A, 0), resumed[0].Phase);
        Assert.Equal(SwitchHoldReason.Dwelling, resumed[0].Reason);
        AllNoSwitch(resumed.Take(4));
        Assert.Equal(A, resumed[4].SwitchTo); // dwell (0.5 s) after the sample that ended the hole
    }

    [Fact]
    public void AHoleInTheStreamFollowedByANonSupportingSampleEndsTheDwellWithTheIdleReason()
    {
        var harness = new DeciderHarness();
        harness.Run(0, 0.25, HandProfile.YawA);
        var weak = harness.Feed(3.25, HandProfile.YawWeakA); // margin 0.2: cannot start a dwell
        Assert.Equal(SwitchPhase.Idle, weak.Phase);
        Assert.Equal(SwitchHoldReason.Uncertain, weak.Reason);
        // The dwell is gone for good, not merely paused.
        Assert.Equal(Dwelling(A, 0), harness.Feed(3.375, HandProfile.YawA).Phase);
    }

    [Fact]
    public void AHoleInTheStreamFollowedByAConfidentLookElsewhereStartsThatDwell()
    {
        var harness = new DeciderHarness();
        harness.Run(0, 0.25, HandProfile.YawA);
        var other = harness.Feed(3.25, HandProfile.YawC);
        Assert.Equal(Dwelling(C, 0), other.Phase);
    }

    // Cancellation by focus

    [Fact]
    public void LookingBackAtTheFocusedDisplayCancelsTheDwell()
    {
        var harness = new DeciderHarness();
        harness.Run(0, 0.125, HandProfile.YawA);
        var back = harness.Feed(0.25, HandProfile.YawB);
        Assert.Null(back.SwitchTo);
        Assert.Equal(SwitchPhase.Idle, back.Phase);
        Assert.Equal(SwitchHoldReason.AlreadyFocused, back.Reason);
        // The old dwell is gone: a return to A restarts the clock.
        Assert.Equal(Dwelling(A, 0), harness.Feed(0.375, HandProfile.YawA).Phase);
    }

    [Fact]
    public void ADwellOnADisplayThatBecomesFocusedIsCancelled()
    {
        var harness = new DeciderHarness();
        harness.Run(0, 0.125, HandProfile.YawA);
        harness.Context = harness.Context with { FocusedDisplay = A }; // for example the user clicked a window on A
        var decision = harness.Feed(0.25, HandProfile.YawA);
        Assert.Null(decision.SwitchTo);
        Assert.Equal(SwitchPhase.Idle, decision.Phase);
        Assert.Equal(SwitchHoldReason.AlreadyFocused, decision.Reason);
        harness.Context = harness.Context with { FocusedDisplay = B };
        Assert.Equal(Dwelling(A, 0), harness.Feed(0.375, HandProfile.YawA).Phase);
    }

    [Fact]
    public void AConfidentLookAtAnotherDisplayReplacesTheDwell()
    {
        var harness = new DeciderHarness();
        harness.Run(0, 0.125, HandProfile.YawA);
        var replaced = harness.Run(0.25, 0.75, HandProfile.YawC);
        Assert.Equal(Dwelling(C, 0), replaced[0].Phase);
        // A's clock would have fired at t = 0.5; C's own clock fires at 0.75.
        AllNoSwitch(replaced.Take(4));
        Assert.Equal(C, replaced[4].SwitchTo);
    }

    // Hysteresis

    [Fact]
    public void AMarginBetweenStayAndEnterCannotStartADwell()
    {
        var harness = new DeciderHarness();
        foreach (var decision in harness.Run(0, 1.5, HandProfile.YawWeakA))
        {
            Assert.Equal(A, decision.Classification.Best);
            Assert.Equal(0.2, decision.Classification.Margin, 1e-12);
            Assert.Null(decision.SwitchTo);
            Assert.Equal(SwitchPhase.Idle, decision.Phase);
            Assert.Equal(SwitchHoldReason.Uncertain, decision.Reason);
        }
    }

    [Fact]
    public void AMarginBetweenStayAndEnterKeepsAnOngoingDwellAliveAndCanComplete()
    {
        var harness = new DeciderHarness();
        harness.Feed(0, HandProfile.YawA); // starts: margin 1
        var weak = harness.Run(0.125, 0.5, HandProfile.YawWeakA);
        double[] progress = [0.25, 0.5, 0.75];
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(Dwelling(A, progress[i]), weak[i].Phase);
            Assert.Null(weak[i].SwitchTo);
        }

        Assert.Equal(A, weak[3].SwitchTo);
    }

    [Fact]
    public void AMarginBelowStayDoesNotSupportTheDwell()
    {
        var harness = new DeciderHarness();
        harness.Feed(0, HandProfile.YawA);
        Assert.Equal(Dwelling(A, 0.25), harness.Feed(0.125, HandProfile.YawVeryWeakA).Phase);
        Assert.Equal(Dwelling(A, 0.5), harness.Feed(0.25, HandProfile.YawVeryWeakA).Phase);
        var dropped = harness.Feed(0.375, HandProfile.YawVeryWeakA); // gap 0.375 > maxGap
        Assert.Equal(SwitchPhase.Idle, dropped.Phase);
        Assert.Equal(SwitchHoldReason.Uncertain, dropped.Reason);
    }

    // Cooldown

    [Fact]
    public void TheCooldownBlocksNewDwellsUntilItEnds()
    {
        var harness = new DeciderHarness();
        var looking = harness.Run(0, 0.5, HandProfile.YawA);
        Assert.Equal(A, looking[4].SwitchTo);
        harness.Context = harness.Context with { FocusedDisplay = A }; // perfect focus

        // Looking at C straight away: blocked until t = 1.25.
        foreach (var decision in harness.Run(0.625, 1.125, HandProfile.YawC))
        {
            Assert.Null(decision.SwitchTo);
            Assert.Equal(SwitchPhase.CoolingDown(1.25), decision.Phase);
            Assert.Equal(SwitchHoldReason.CoolingDown, decision.Reason);
        }

        // The cooldown ends at t == until, and a dwell starts only then.
        var afterwards = harness.Run(1.25, 1.75, HandProfile.YawC);
        Assert.Equal(Dwelling(C, 0), afterwards[0].Phase);
        AllNoSwitch(afterwards.Take(4));
        Assert.Equal(C, afterwards[4].SwitchTo);
    }

    [Fact]
    public void TheCooldownAlsoBlocksWhenThereIsNoFace()
    {
        var harness = new DeciderHarness();
        harness.Run(0, 0.5, HandProfile.YawA);
        var blocked = harness.Feed(0.625, null);
        Assert.Equal(SwitchPhase.CoolingDown(1.25), blocked.Phase);
        Assert.Equal(SwitchHoldReason.CoolingDown, blocked.Reason);
    }

    // Focus lock

    [Fact]
    public void FocusLockBlocksSwitchingAndCancelsTheDwell()
    {
        var harness = new DeciderHarness();
        harness.Run(0, 0.125, HandProfile.YawA);
        harness.Context = harness.Context with { IsFocusLocked = true };
        foreach (var decision in harness.Run(0.25, 0.75, HandProfile.YawA)) // 0.5 would have completed it
        {
            Assert.Null(decision.SwitchTo);
            Assert.Equal(SwitchPhase.Locked, decision.Phase);
            Assert.Equal(SwitchHoldReason.FocusLocked, decision.Reason);
            Assert.Equal(A, decision.Classification.Best); // the classification is still reported
        }

        // Unlocking starts a fresh dwell.
        harness.Context = harness.Context with { IsFocusLocked = false };
        var unlocked = harness.Run(0.875, 1.375, HandProfile.YawA);
        Assert.Equal(Dwelling(A, 0), unlocked[0].Phase);
        AllNoSwitch(unlocked.Take(4));
        Assert.Equal(A, unlocked[4].SwitchTo);
    }

    [Fact]
    public void FocusLockAlsoClearsTheCooldown()
    {
        var harness = new DeciderHarness();
        harness.Run(0, 0.5, HandProfile.YawA); // fires at 0.5, cooldown until 1.25
        harness.Context = harness.Context with { FocusedDisplay = A, IsFocusLocked = true };
        Assert.Equal(SwitchPhase.Locked, harness.Feed(0.625, HandProfile.YawC).Phase);
        harness.Context = harness.Context with { IsFocusLocked = false };
        // Still before t = 1.25, but the lock reset the machine, so a dwell starts at once.
        Assert.Equal(Dwelling(C, 0), harness.Feed(0.75, HandProfile.YawC).Phase);
    }

    // Targets

    [Fact]
    public void ANearestDisplayOutsideTheTargetsNeverStartsADwell()
    {
        var harness = new DeciderHarness();
        harness.Context = harness.Context with { Targets = new HashSet<DisplayId> { B, C } };
        foreach (var decision in harness.Run(0, 1, HandProfile.YawA))
        {
            Assert.Null(decision.SwitchTo);
            Assert.Equal(SwitchPhase.Idle, decision.Phase);
            Assert.Equal(SwitchHoldReason.NotATarget, decision.Reason);
        }

        // A target display still works.
        harness.Context = harness.Context with { FocusedDisplay = A };
        var toC = harness.Run(1.125, 1.625, HandProfile.YawC);
        Assert.Equal(Dwelling(C, 0), toC[0].Phase);
        Assert.Equal(C, toC[4].SwitchTo);
    }

    [Fact]
    public void ADwellDoesNotSurviveItsDisplayLeavingTheTargets()
    {
        var harness = new DeciderHarness();
        harness.Run(0, 0.125, HandProfile.YawA);
        harness.Context = harness.Context with { Targets = new HashSet<DisplayId> { B, C } };
        var first = harness.Feed(0.25, HandProfile.YawA); // treated as a gap: no support, no fire
        Assert.Null(first.SwitchTo);
        Assert.Equal(Dwelling(A, 0.5), first.Phase);
        harness.Feed(0.375, HandProfile.YawA);
        var atDwell = harness.Feed(0.5, HandProfile.YawA); // t - start == dwell, but A is no longer a target
        Assert.Null(atDwell.SwitchTo);
        Assert.Equal(SwitchPhase.Idle, atDwell.Phase);
        Assert.Equal(SwitchHoldReason.NotATarget, atDwell.Reason);
        Assert.Equal(SwitchHoldReason.NotATarget, harness.Feed(0.625, HandProfile.YawA).Reason);
    }

    [Fact]
    public void ATargetRunnerUpDoesNotRescueANonTargetNearestDisplay()
    {
        var harness = new DeciderHarness();
        harness.Context = harness.Context with { Targets = new HashSet<DisplayId> { B } }; // only B; nearest A, runner-up B
        var decision = harness.Feed(0, HandProfile.YawWeakA);
        Assert.Equal(B, decision.Classification.RunnerUp);
        Assert.Equal(SwitchHoldReason.NotATarget, decision.Reason);
    }

    // Focus state

    [Fact]
    public void LookingAtTheFocusedDisplayIsAlreadyFocused()
    {
        var harness = new DeciderHarness();
        foreach (var decision in harness.Run(0, 1, HandProfile.YawB))
        {
            Assert.Null(decision.SwitchTo);
            Assert.Equal(SwitchPhase.Idle, decision.Phase);
            Assert.Equal(SwitchHoldReason.AlreadyFocused, decision.Reason);
        }
    }

    [Fact]
    public void ANilFocusedDisplayAllowsAnySwitch()
    {
        var harness = DeciderHarness.WithoutFocus();
        var first = harness.Run(0, 0.5, HandProfile.YawB);
        Assert.Equal(B, first[4].SwitchTo);
        // Focus is still unknown. After the cooldown (until 1.25) another look dwells and switches.
        var second = harness.Run(0.625, 1.75, HandProfile.YawC);
        Assert.Equal([C], second.Where(d => d.SwitchTo is not null).Select(d => d.SwitchTo!.Value));
        Assert.Equal(9, second.FindIndex(d => d.SwitchTo is not null)); // t = 1.75: started at 1.25 + 0.5
    }

    // Idle reasons

    [Fact]
    public void IdleReasonsFollowTheContractOrder()
    {
        var harness = new DeciderHarness();
        Assert.Equal(SwitchHoldReason.NoFace, harness.Feed(0, null).Reason);
        Assert.Equal(SwitchHoldReason.NoFace, harness.Feed(0.125, HandProfile.YawA, confidence: 0.3).Reason);
        Assert.Equal(SwitchHoldReason.Elsewhere, harness.Feed(0.25, HandProfile.YawElsewhere).Reason);
        Assert.Equal(SwitchHoldReason.Uncertain, harness.Feed(0.375, HandProfile.YawWeakA).Reason);

        // Not a target outranks already focused, which outranks uncertain.
        harness.Context = new SwitchContext(A, targets: new HashSet<DisplayId> { B });
        Assert.Equal(SwitchHoldReason.NotATarget, harness.Feed(0.5, HandProfile.YawA).Reason);
        harness.Context = new SwitchContext(A);
        Assert.Equal(SwitchHoldReason.AlreadyFocused, harness.Feed(0.625, HandProfile.YawWeakA).Reason);
    }

    [Fact]
    public void EveryDecisionCarriesTheClassificationOfItsSample()
    {
        var harness = new DeciderHarness();
        var classifier = new PoseClassifier(HandProfile.Profile, HandProfile.Settings);
        TimedPose[] samples =
        [
            Poses.Sample(0, HandProfile.YawA),
            Poses.Sample(0.125, null),
            Poses.Sample(0.25, HandProfile.YawElsewhere),
            Poses.Sample(0.375, HandProfile.YawWeakA),
        ];
        foreach (var sample in samples)
        {
            var decision = harness.Machine.Process(sample, harness.Context);
            Assert.Equal(classifier.Classify(sample.Pose), decision.Classification);
        }
    }

    // Time and reset

    [Fact]
    public void ABackwardsTimestampResetsTheMachine()
    {
        var harness = new DeciderHarness();
        harness.Run(10, 10.125, HandProfile.YawA);
        // A new stream restarts the clock at 2.0: the dwell begins again instead of continuing.
        var restarted = harness.Run(2, 2.5, HandProfile.YawA);
        Assert.Null(restarted[0].SwitchTo);
        Assert.Equal(Dwelling(A, 0), restarted[0].Phase);
        AllNoSwitch(restarted.Take(4));
        Assert.Equal(A, restarted[4].SwitchTo);
    }

    [Fact]
    public void ABackwardsTimestampClearsTheCooldown()
    {
        var harness = new DeciderHarness();
        var looking = harness.Run(10, 10.5, HandProfile.YawA); // fires; cooldown until 11.25
        Assert.Equal(A, looking[4].SwitchTo);
        harness.Context = harness.Context with { FocusedDisplay = A };
        var afterRestart = harness.Feed(3, HandProfile.YawC);
        Assert.Equal(Dwelling(C, 0), afterRestart.Phase);
    }

    [Fact]
    public void AnEqualTimestampIsNotBackwards()
    {
        var harness = new DeciderHarness();
        harness.Run(1, 1.25, HandProfile.YawA);
        var repeated = harness.Feed(1.25, HandProfile.YawA);
        Assert.Equal(Dwelling(A, 0.5), repeated.Phase);
    }

    [Fact]
    public void ResetDropsADwell()
    {
        var harness = new DeciderHarness();
        harness.Run(5, 5.25, HandProfile.YawA);
        harness.Machine.Reset();
        Assert.Equal(Dwelling(A, 0), harness.Feed(5.375, HandProfile.YawA).Phase);
    }

    [Fact]
    public void ResetDropsTheCooldown()
    {
        var harness = new DeciderHarness();
        var looking = harness.Run(0, 0.5, HandProfile.YawA);
        Assert.Equal(A, looking[4].SwitchTo);
        harness.Context = harness.Context with { FocusedDisplay = A };
        harness.Machine.Reset();
        Assert.Equal(Dwelling(C, 0), harness.Feed(0.625, HandProfile.YawC).Phase);
    }

    // Time tolerance (epsilon)

    [Fact]
    public void EpsilonIsOneMicrosecond() => Assert.Equal(1e-6, SwitchStateMachine.TimeTolerance);

    [Fact]
    public void ADwellIsCompleteWithinEpsilonBeforeTheDwellButNotMoreThanThat()
    {
        foreach (var (offset, shouldFire) in new[] { (-0.5 * Epsilon, true), (-2 * Epsilon, false) })
        {
            var harness = new DeciderHarness();
            harness.Run(0, 0.375, HandProfile.YawA);
            var decision = harness.Feed(0.5 + offset, HandProfile.YawA);
            Assert.Equal(shouldFire, decision.SwitchTo == A);
        }
    }

    [Fact]
    public void ACooldownIsOverWithinEpsilonBeforeItsEndButNotMoreThanThat()
    {
        foreach (var (offset, blocked) in new[] { (-0.5 * Epsilon, false), (-2 * Epsilon, true) })
        {
            var harness = new DeciderHarness();
            harness.Run(0, 0.5, HandProfile.YawA); // fires at 0.5, cooldown until 1.25
            harness.Context = harness.Context with { FocusedDisplay = A };
            var decision = harness.Feed(1.25 + offset, HandProfile.YawC);
            Assert.Equal(blocked, decision.Reason == SwitchHoldReason.CoolingDown);
            Assert.Equal(!blocked, decision.Phase == Dwelling(C, 0));
        }
    }

    [Fact]
    public void AGapIsExceededOnlyBeyondMaxGapPlusEpsilon()
    {
        foreach (var (offset, survives) in new[] { (0.5 * Epsilon, true), (2 * Epsilon, false) })
        {
            var harness = new DeciderHarness();
            harness.Run(0, 0.125, HandProfile.YawA); // last support at 0.125
            harness.Feed(0.25, null);
            var decision = harness.Feed(0.125 + 0.25 + offset, null); // gap = maxGap + offset
            Assert.Equal(survives, decision.Reason == SwitchHoldReason.Dwelling);
            Assert.Equal(!survives, decision.Phase == SwitchPhase.Idle);
        }
    }

    // Zero dwell

    [Fact]
    public void ADwellOfZeroFiresImmediately()
    {
        var harness = new DeciderHarness(HandProfile.Settings with { Dwell = 0 });
        var fired = harness.Feed(0, HandProfile.YawA);
        Assert.Equal(A, fired.SwitchTo);
        Assert.Equal(SwitchPhase.CoolingDown(0.75), fired.Phase);
        Assert.Null(fired.Reason);
    }

    [Fact]
    public void ADwellOfZeroStillNeedsTheEnterMarginAndRespectsTheCooldown()
    {
        var harness = new DeciderHarness(HandProfile.Settings with { Dwell = 0 });
        Assert.Null(harness.Feed(0, HandProfile.YawWeakA).SwitchTo); // margin 0.2 < enter
        Assert.Equal(A, harness.Feed(0.125, HandProfile.YawA).SwitchTo);
        harness.Context = harness.Context with { FocusedDisplay = A };
        var blocked = harness.Run(0.25, 0.75, HandProfile.YawC);
        Assert.All(blocked, d =>
        {
            Assert.Null(d.SwitchTo);
            Assert.Equal(SwitchHoldReason.CoolingDown, d.Reason);
        });
        Assert.Equal(C, harness.Feed(0.875, HandProfile.YawC).SwitchTo); // cooldown over (until 0.875): fires at once
    }
}
