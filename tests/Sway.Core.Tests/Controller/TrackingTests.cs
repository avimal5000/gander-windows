using Sway.Core.Controller;
using Sway.Core.Platform;
using Sway.Core.Runtime;

namespace Sway.Core.Tests.Controller;

/// <summary>Port of the Mac AppModelTrackingTests ("AppModel tracking loop").</summary>
public class TrackingTests
{
    private static readonly DisplayId Left = Fixtures.LeftId;
    private static readonly DisplayId Right = Fixtures.RightId;
    private static readonly DisplayId Far = Fixtures.FarId;

    /// <summary>A calibrated controller with Tracking on, the camera running, and the first focused-display poll done.</summary>
    private static async Task<Harness> TrackingHarness(DisplayLayout? layout = null)
    {
        var h = Harness.Make(layout: layout ?? Fixtures.TwoDisplays, calibrated: true);
        await h.StartTracking();
        h.Drain();
        return h;
    }

    /// <summary>
    /// A decider that dwells on the target from <paramref name="dwellFrom"/> and fires at <paramref name="fireAt"/>, then
    /// keeps asking to switch. Like the real one it goes idle while locked, unless <paramref name="ignoresLock"/> makes it misbehave.
    /// </summary>
    internal static void ScriptLook(Harness h, DisplayId target, double dwellFrom = 1.0, double fireAt = 1.4, bool ignoresLock = false) =>
        h.Engine.Script.Rule = (sample, context) =>
        {
            if (context.IsFocusLocked && !ignoresLock)
            {
                return Fixtures.Decision(phase: SwitchPhase.Locked, reason: SwitchHoldReason.FocusLocked);
            }

            var look = Fixtures.Classification(best: target);
            if (sample.Timestamp >= fireAt)
            {
                return Fixtures.Decision(switchTo: target, phase: SwitchPhase.CoolingDown(sample.Timestamp + 0.75), classification: look);
            }

            if (sample.Timestamp >= dwellFrom)
            {
                var progress = (sample.Timestamp - dwellFrom) / (fireAt - dwellFrom);
                return Fixtures.Decision(phase: SwitchPhase.Dwelling(target, progress), classification: look);
            }

            return Fixtures.Decision(classification: look);
        };

    internal static FocusResult FocusedRight(string app = "Edge", int ms = 120) =>
        new FocusResult.Focused(Fixtures.Window(7, app, Fixtures.Right), FocusMethod.AttachInput, ms);

    private static void Feed(Harness h, params double[] times) => h.FeedBurst(times);

    // MARK: Switching

    [Fact]
    public async Task AHeldLookFiresExactlyOneFocusRequestForTheRightDisplay()
    {
        using var h = await TrackingHarness();
        h.Focus.Result = FocusedRight();
        ScriptLook(h, Right);

        Feed(h, 1.0, 1.2, 1.4, 1.5, 1.6);

        Assert.Equal([Right], h.Focus.FocusCalls);
        Assert.Equal([Fixtures.TwoDisplays], h.Focus.FocusLayouts);
        Assert.Equal(Right, h.Controller.TrackingFocus);
        Assert.False(h.Controller.IsSwitching);
        Assert.Single(h.Controller.SwitchLog);
    }

    [Fact]
    public async Task TheLogNamesTheAppAndTheDisplayAndHowLongTheLookWasHeldNeverTheTitle()
    {
        using var h = await TrackingHarness();
        h.Focus.Result = FocusedRight("Edge", 120);
        ScriptLook(h, Right);

        Feed(h, 1.0, 1.2, 1.4);

        var entry = Assert.Single(h.Controller.SwitchLog);
        Assert.Equal("Focused Edge on Right in 120 ms, held 0.40 s", entry.Text);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000), entry.Time);
    }

    [Fact]
    public async Task TheNextSampleCarriesTheNewFocusAndTheTargets()
    {
        using var h = await TrackingHarness();
        h.Focus.Result = FocusedRight();
        ScriptLook(h, Right);
        Feed(h, 1.0, 1.2, 1.4);

        h.Feed(1.5);

        Assert.Equal(new SwitchContext(Right, false, null), h.Engine.LastRecorder!.Contexts.Last());
    }

    [Fact]
    public async Task NoOtherRequestStartsWhileOneIsInFlightAndTheDeciderKeepsItsCooldown()
    {
        using var h = await TrackingHarness();
        h.Focus.HoldFocusRequests();
        ScriptLook(h, Right);
        Feed(h, 1.0, 1.2, 1.4);

        Assert.True(h.Focus.IsHoldingFocus);
        Assert.True(h.Controller.IsSwitching);
        Assert.Equal(TrackingHold.Switching, h.Controller.TrackingHold);
        Assert.Equal(Right, h.Controller.TrackingFocus);
        Feed(h, 1.5, 1.6);

        // Not a lock: a lock would cancel the cooldown that follows a switch.
        var contexts = h.Engine.LastRecorder!.Contexts.TakeLast(2).ToList();
        Assert.Equal([false, false], contexts.Select(c => c.IsFocusLocked));
        Assert.Equal([Right, Right], contexts.Select(c => c.FocusedDisplay));
        Assert.Equal([Right], h.Focus.FocusCalls);
        h.Focus.Release();
        h.Drain();
        Assert.False(h.Controller.IsSwitching);
        Assert.Null(h.Controller.TrackingHold);
    }

    [Fact]
    public async Task AMisbehavingDeciderStillCannotStartASecondRequest()
    {
        using var h = await TrackingHarness();
        h.Focus.HoldFocusRequests();
        ScriptLook(h, Right, ignoresLock: true);
        Feed(h, 1.4, 1.5, 1.6);

        Assert.Equal([Right], h.Focus.FocusCalls);
        h.Focus.Release();
        h.Drain();
    }

    // MARK: Holds

    [Fact]
    public async Task NoRequestWhileFocusLockIsOn()
    {
        using var h = await TrackingHarness();
        h.Controller.SetFocusLock(true);
        ScriptLook(h, Right, ignoresLock: true);

        Feed(h, 1.0, 1.4, 1.6);

        Assert.Equal(TrackingHold.FocusLock, h.Controller.TrackingHold);
        Assert.All(h.Engine.LastRecorder!.Contexts, c => Assert.True(c.IsFocusLocked));
        Assert.Empty(h.Focus.FocusCalls);
        Assert.Empty(h.Controller.SwitchLog);
    }

    [Fact]
    public async Task NoRequestWhileSwayIsInFront()
    {
        using var h = await TrackingHarness();
        h.Controller.SetSwayInFront(true);
        ScriptLook(h, Right, ignoresLock: true);

        Feed(h, 1.0, 1.4, 1.6);

        Assert.Equal(TrackingHold.SwayInFront, h.Controller.TrackingHold);
        Assert.All(h.Engine.LastRecorder!.Contexts, c => Assert.True(c.IsFocusLocked));
        Assert.Empty(h.Focus.FocusCalls);

        h.Controller.SetSwayInFront(false);
        h.Feed(2.0);
        Assert.Equal([Right], h.Focus.FocusCalls);
    }

    [Fact]
    public async Task TheHoldReasonsHavePriority()
    {
        using var h = await TrackingHarness();
        Assert.Null(h.Controller.TrackingHold);

        // The Core TrackingHolds.Resolve order, seen through the controller's own inputs.
        h.Focus.HoldFocusRequests();
        ScriptLook(h, Right);
        Feed(h, 1.4);
        Assert.Equal(TrackingHold.Switching, h.Controller.TrackingHold);
        h.Displays.Notify();
        h.Drain();
        // The display hold resets the loop, which drops the request in flight.
        Assert.Equal(TrackingHold.DisplaysChanging, h.Controller.TrackingHold);
        h.Controller.ActiveDesktopChanged();
        Assert.Equal(TrackingHold.DisplaysChanging, h.Controller.TrackingHold);
        h.Controller.SetSwayInFront(true);
        Assert.Equal(TrackingHold.SwayInFront, h.Controller.TrackingHold);
        h.Controller.SetFocusLock(true);
        Assert.Equal(TrackingHold.FocusLock, h.Controller.TrackingHold);
    }

    // MARK: Results

    [Fact]
    public async Task ADisplayWithNoWindowIsTreatedAsFocusedSoItIsNotRetried()
    {
        using var h = await TrackingHarness();
        h.Focus.Result = new FocusResult.NoWindow(Right);
        ScriptLook(h, Right);

        Feed(h, 1.4, 1.5);
        h.Feed(2.5);

        Assert.Equal("No window on Right", h.Controller.SwitchLog[0].Text);
        Assert.Equal(Right, h.Controller.TrackingFocus);
        Assert.Equal(Right, h.Engine.LastRecorder!.Contexts.Last().FocusedDisplay);
    }

    [Fact]
    public async Task TheOverrideEndsWhenThePollSeesFocusSomewhereElse()
    {
        using var h = await TrackingHarness();
        h.Focus.Result = new FocusResult.NoWindow(Right);
        ScriptLook(h, Right);
        h.Feed(1.4);
        Assert.Equal(Right, h.Controller.TrackingFocus);

        h.Controller.TrackingTick();
        h.Drain();
        Assert.Equal(Right, h.Controller.TrackingFocus);

        h.Focus.FocusedDisplay = Left;
        h.Controller.TrackingTick();
        h.Drain();
        Assert.Equal(Left, h.Controller.TrackingFocus);
    }

    [Fact]
    public async Task TheOverrideEndsWhenTheLookMovesOnToAnotherDisplay()
    {
        using var h = await TrackingHarness();
        h.Focus.Result = new FocusResult.NoWindow(Right);
        ScriptLook(h, Right);
        h.Feed(1.4);
        Assert.Equal(Right, h.Controller.TrackingFocus);

        h.Engine.Script.Rule = (_, _) => Fixtures.Decision(classification: Fixtures.Classification(best: Left, margin: 0.8));
        h.Feed(2.0);

        Assert.Null(h.Controller.TrackingFocus);
    }

    [Fact]
    public async Task AFailureIsLoggedWithItsReason()
    {
        using var h = await TrackingHarness();
        h.Focus.Result = new FocusResult.Failed(Fixtures.Window(on: Fixtures.Right), "Focus did not move to Edge");
        ScriptLook(h, Right);

        h.Feed(1.4);

        Assert.Equal("Failed: Focus did not move to Edge", h.Controller.SwitchLog[0].Text);
        Assert.Equal(Right, h.Controller.TrackingFocus);
    }

    [Fact]
    public async Task TheSwitchLogKeepsTheNewestTenNewestFirst()
    {
        using var h = await TrackingHarness();
        h.Focus.Result = new FocusResult.NoWindow(Right);
        h.Engine.Script.Rule = (sample, _) => Fixtures.Decision(switchTo: Right, phase: SwitchPhase.CoolingDown(sample.Timestamp + 1));

        for (var index = 1; index <= 12; index++)
        {
            h.Feed(index);

            // Starts the next request clean: a display hold resets the loop.
            h.Controller.ActiveDesktopChanged();
            h.Displays.Notify();
            h.Drain();
            h.Advance(4);
        }

        Assert.Equal(TrackingController.SwitchLogLimit, h.Controller.SwitchLog.Count);
        var ids = h.Controller.SwitchLog.Select(e => e.Id).ToList();
        Assert.Equal(ids.OrderByDescending(i => i), ids);
        Assert.Equal(10, ids.Distinct().Count());
    }

    // MARK: Hold time

    [Fact]
    public async Task TheHoldTimeCountsFromTheStartOfTheDwell()
    {
        using var h = await TrackingHarness();
        h.Focus.Result = FocusedRight();
        h.Engine.Script.Rule = (sample, _) => sample.Timestamp switch
        {
            1.0 => Fixtures.Decision(phase: SwitchPhase.Dwelling(Right, 0)),
            1.1 => Fixtures.Decision(reason: SwitchHoldReason.NoFace),
            2.0 => Fixtures.Decision(phase: SwitchPhase.Dwelling(Right, 0)),
            2.4 => Fixtures.Decision(switchTo: Right, phase: SwitchPhase.CoolingDown(3.15)),
            _ => Fixtures.Decision(),
        };

        Feed(h, 1.0, 1.1, 2.0, 2.4);

        Assert.EndsWith("held 0.40 s", h.Controller.SwitchLog[0].Text);
    }

    [Fact]
    public async Task ADwellBegunAgainOnTheSameDisplayAfterAGapStartsItsHoldTimeAgain()
    {
        // Mac CONTRACT 6.6 step 4(a2): the phase goes dwelling(progress 0.5) -> dwelling(progress 0).
        using var h = await TrackingHarness();
        h.Focus.Result = FocusedRight();
        h.Engine.Script.Rule = (sample, _) => sample.Timestamp switch
        {
            1.0 => Fixtures.Decision(phase: SwitchPhase.Dwelling(Right, 0)),
            1.2 => Fixtures.Decision(phase: SwitchPhase.Dwelling(Right, 0.5)),
            2.0 => Fixtures.Decision(phase: SwitchPhase.Dwelling(Right, 0)),
            2.2 => Fixtures.Decision(phase: SwitchPhase.Dwelling(Right, 0.5)),
            2.4 => Fixtures.Decision(switchTo: Right, phase: SwitchPhase.CoolingDown(3.15)),
            _ => Fixtures.Decision(),
        };

        Feed(h, 1.0, 1.2, 2.0, 2.2, 2.4);

        Assert.EndsWith("held 0.40 s", h.Controller.SwitchLog[0].Text);
    }

    [Fact]
    public async Task ADwellThatMovesToAnotherDisplayStartsItsHoldTimeAgain()
    {
        using var h = await TrackingHarness();
        h.Focus.Result = FocusedRight();
        h.Engine.Script.Rule = (sample, _) => sample.Timestamp switch
        {
            1.0 => Fixtures.Decision(phase: SwitchPhase.Dwelling(Left, 0)),
            1.3 => Fixtures.Decision(phase: SwitchPhase.Dwelling(Right, 0)),
            1.7 => Fixtures.Decision(switchTo: Right, phase: SwitchPhase.CoolingDown(2.45)),
            _ => Fixtures.Decision(),
        };

        Feed(h, 1.0, 1.3, 1.7);

        Assert.EndsWith("held 0.40 s", h.Controller.SwitchLog[0].Text);
    }

    [Fact]
    public async Task ASwitchWithoutAnObservedDwellHasZeroHoldTime()
    {
        using var h = await TrackingHarness();
        h.Focus.Result = FocusedRight();
        h.Engine.Script.Rule = (_, _) => Fixtures.Decision(switchTo: Right, phase: SwitchPhase.CoolingDown(9));

        h.Feed(5);

        Assert.EndsWith("held 0.00 s", h.Controller.SwitchLog[0].Text);
    }

    // MARK: Resets

    [Fact]
    public async Task ALayoutChangeResetsTheLoopAndALateResultIsIgnored()
    {
        using var h = Harness.Make(layout: Fixtures.ThreeDisplays, calibrated: true);
        await h.StartTracking();
        h.Focus.HoldFocusRequests();
        ScriptLook(h, Right);
        h.Feed(1.4);
        Assert.True(h.Focus.IsHoldingFocus);
        Assert.True(h.Controller.IsSwitching);

        h.Displays.Change(Fixtures.TwoDisplays);
        h.Drain();

        Assert.False(h.Controller.IsSwitching);
        Assert.Null(h.Controller.TrackingFocus);
        h.Focus.Result = FocusedRight();
        h.Focus.Release();
        h.Drain();
        Assert.Empty(h.Controller.SwitchLog);
        Assert.Null(h.Controller.TrackingFocus);
    }

    [Fact]
    public async Task ALayoutChangeThatKeepsTheCalibrationStillResetsTheDecider()
    {
        using var h = await TrackingHarness();
        h.Engine.Script.Rule = (_, _) => Fixtures.Decision(phase: SwitchPhase.Dwelling(Right, 0.5));
        h.Feed(1);
        var recorder = h.Engine.LastRecorder!;
        var resets = recorder.ResetCount;
        Assert.NotNull(h.Controller.LastDecision);
        var moved = new DisplayInfo(Right, 12, "Right", new DisplayRect(1920, 0, 1280, 720), false, false);

        h.Displays.Change(new DisplayLayout([Fixtures.Left, moved]));
        h.Drain();

        Assert.True(recorder.ResetCount > resets);
        Assert.Null(h.Controller.LastDecision);
    }

    [Fact]
    public async Task AnInterruptionResetsTheLoopAndARestartMakesAFreshDecider()
    {
        using var h = await TrackingHarness();
        h.Focus.HoldFocusRequests();
        ScriptLook(h, Right);
        h.Feed(1.4);
        Assert.True(h.Focus.IsHoldingFocus);
        var recorder = h.Engine.LastRecorder!;
        var resetsBefore = recorder.ResetCount;

        h.ReportStatus(new CaptureStatus.Interrupted("Camera disconnected"));

        Assert.False(h.Controller.IsSwitching);
        Assert.True(recorder.ResetCount > resetsBefore);
        Assert.Null(h.Controller.LastDecision);
        h.Focus.Release();
        h.Drain();
        Assert.Empty(h.Controller.SwitchLog);

        // Running again, then a restart: a fresh decider.
        h.ReportRunning();
        var before = h.Engine.Recorders.Count;
        await h.SetTracking(false);
        await h.SetTracking(true);
        Assert.Equal(before + 1, h.Engine.Recorders.Count);
    }

    [Fact]
    public async Task TurningTrackingOffResetsAndStopsDeciding()
    {
        using var h = await TrackingHarness();
        ScriptLook(h, Right);
        h.Feed(1.2);
        var recorder = h.Engine.LastRecorder!;

        await h.SetTracking(false);
        h.Feed(1.4);

        Assert.True(recorder.ResetCount >= 1);
        Assert.Single(recorder.Samples);
        Assert.Null(h.Controller.LastDecision);
        Assert.Empty(h.Focus.FocusCalls);
    }

    // MARK: A calibration run holds tracking (the Mac gate test)

    [Fact]
    public async Task ACalibrationRunHoldsTrackingWhileItRuns()
    {
        using var h = await TrackingHarness();
        var recorder = h.Engine.LastRecorder!;
        ScriptLook(h, Right);

        await h.Controller.SetCalibrationRunningAsync(true);
        h.Drain();
        h.ReportRunning();
        h.Feed(1.4);

        Assert.Empty(recorder.Samples);
        Assert.Empty(h.Focus.FocusCalls);
        Assert.False(h.Controller.IsTrackingActive);
        Assert.Equal(TrackingStatusKind.Calibrating, h.Controller.TrackingStatus.Kind);
    }

    // MARK: Targets (SPEC D22)

    [Fact]
    public async Task TwoAndThreeDisplaysAreAllTargetsAndAFourthIsLeftOut()
    {
        var cases = new (DisplayLayout Layout, HashSet<DisplayId>? Expected)[]
        {
            (Fixtures.TwoDisplays, null),
            (Fixtures.ThreeDisplays, null),
            (Fixtures.FourDisplays, [Fixtures.LeftId, Fixtures.RightId, Fixtures.FarId]),
        };
        foreach (var (layout, expected) in cases)
        {
            using var h = await TrackingHarness(layout);
            h.Feed(1);
            var targets = h.Engine.LastRecorder!.Contexts.Last().Targets;
            if (expected is null)
            {
                Assert.Null(targets);
            }
            else
            {
                Assert.True(expected.SetEquals(targets!), $"{layout.Displays.Count} displays");
            }
        }
    }

    [Fact]
    public async Task UnplugPartOfACalibrationAndTheRemainingDisplaysAreTheTargets()
    {
        using var h = await TrackingHarness(Fixtures.FourDisplays);

        h.Displays.Change(Fixtures.ThreeDisplays);
        h.Drain();
        h.Feed(1);

        Assert.Null(h.Engine.LastRecorder!.Contexts.Last().Targets);
        Assert.True(new HashSet<DisplayId> { Left, Right, Far }.SetEquals(h.Engine.LastRecorder.Profile.DisplayIds));
    }

    // MARK: Focused-display poll

    [Fact]
    public async Task ThePollRunsOnlyWhileTrackingRuns()
    {
        using var h = Harness.Make(calibrated: true);

        h.Controller.TrackingTick();
        Assert.Equal(0, h.Focus.FocusedDisplayCalls);

        await h.SetTracking(true);
        h.Controller.TrackingTick();
        Assert.Equal(0, h.Focus.FocusedDisplayCalls);

        h.ReportRunning();
        Assert.Equal(1, h.Focus.FocusedDisplayCalls);

        h.Controller.TrackingTick();
        h.Drain();
        Assert.Equal(2, h.Focus.FocusedDisplayCalls);

        await h.SetTracking(false);
        h.Controller.TrackingTick();
        h.Drain();
        Assert.Equal(2, h.Focus.FocusedDisplayCalls);
    }

    [Fact]
    public async Task TheCameraLeavingTheRunningStateStopsThePoll()
    {
        using var h = await TrackingHarness();
        var calls = h.Focus.FocusedDisplayCalls;

        h.ReportStatus(new CaptureStatus.Suspended(SuspendReason.SystemSleep));
        h.Controller.TrackingTick();
        h.Drain();
        Assert.Equal(calls, h.Focus.FocusedDisplayCalls);

        h.ReportRunning();
        Assert.Equal(calls + 1, h.Focus.FocusedDisplayCalls);
    }

    [Fact]
    public async Task ATickWhileAPollIsStillRunningDoesNotStartAnother()
    {
        using var h = await TrackingHarness();
        var calls = h.Focus.FocusedDisplayCalls;
        h.Focus.HoldPolls();

        h.Controller.TrackingTick();
        h.Controller.TrackingTick();
        h.Controller.TrackingTick();
        h.Focus.ReleasePoll();
        h.Drain();

        Assert.Equal(calls + 1, h.Focus.FocusedDisplayCalls);
    }

    [Fact]
    public async Task AForegroundChangeTriggersOnePoll()
    {
        using var h = await TrackingHarness();
        var calls = h.Focus.FocusedDisplayCalls;
        h.Focus.FocusedDisplay = Right;

        h.Controller.ForegroundChanged();
        h.Drain();

        Assert.Equal(calls + 1, h.Focus.FocusedDisplayCalls);
        Assert.Equal(Right, h.Controller.TrackingFocus);
    }

    [Fact]
    public void AForegroundChangeWhileNotTrackingPollsNothing()
    {
        using var h = Harness.Make(calibrated: true);

        h.Controller.ForegroundChanged();
        h.Drain();

        Assert.Equal(0, h.Focus.FocusedDisplayCalls);
    }

    [Fact]
    public async Task ThePolledDisplayIsTheFocusTheDeciderSees()
    {
        using var h = Harness.Make(calibrated: true);
        h.Focus.FocusedDisplay = Left;
        await h.StartTracking();

        h.Feed(1);

        Assert.Equal(Left, h.Controller.TrackingFocus);
        Assert.Equal(Left, h.Engine.LastRecorder!.Contexts.Last().FocusedDisplay);
    }

    [Fact]
    public async Task APollThatStartedBeforeASwitchCannotUndoIt()
    {
        using var h = await TrackingHarness();
        h.Focus.Result = FocusedRight();
        ScriptLook(h, Right);
        h.Focus.FocusedDisplay = Left;
        h.Focus.HoldPolls();

        h.Controller.TrackingTick(); // Starts a poll that will answer "left".
        h.Feed(1.4); // Starts and finishes a switch to right before that poll is applied.
        h.Focus.ReleasePoll();
        h.Drain();

        Assert.Equal(Right, h.Controller.TrackingFocus);
    }

    [Fact]
    public async Task ThePollRepeatsOnTheSchedulerWhileTrackingRuns()
    {
        using var h = Harness.Make(calibrated: true, focusPollInterval: 1);
        await h.SetTracking(true);
        h.ReportRunning();
        Assert.Equal(1, h.Focus.FocusedDisplayCalls);

        h.Advance(1);
        Assert.Equal(2, h.Focus.FocusedDisplayCalls);
        h.Advance(1);
        Assert.Equal(3, h.Focus.FocusedDisplayCalls);

        await h.SetTracking(false);
        h.Advance(3);
        Assert.Equal(3, h.Focus.FocusedDisplayCalls);
        Assert.False(h.Scheduler.HasPending);
    }

    // MARK: Safety

    [Fact]
    public async Task TrackingNeverReadsTheWindowListOrChangesTheLockState()
    {
        using var h = await TrackingHarness();
        h.Focus.Result = FocusedRight();
        ScriptLook(h, Right);

        Feed(h, 1.0, 1.4, 2.5, 3.5);

        Assert.Equal([Right], h.Focus.FocusCalls);
        Assert.False(h.Controller.FocusLocked);
    }

    // MARK: What the always-alive views read

    [Fact]
    public async Task TheStatusOnlyChangesWhenItChangesNeverAtTheFrameRate()
    {
        using var h = await TrackingHarness();
        ScriptLook(h, Right, dwellFrom: 1000, fireAt: 2000);
        h.Feed(0.1);
        Assert.Equal(TrackingStatus.Tracking, h.Controller.TrackingStatus);

        var before = h.StateChangedCount;
        for (var index = 1; index <= 60; index++)
        {
            h.Feed(0.1 + (index / 15.0), yaw: index % 7, pitch: index % 5);
        }

        Assert.Equal(before, h.StateChangedCount);
    }

    [Fact]
    public async Task AFaceComingAndGoingDoesChangeTheStatus()
    {
        using var h = await TrackingHarness();
        h.Feed(1);
        Assert.Equal(TrackingStatus.Tracking, h.Controller.TrackingStatus);
        var before = h.StateChangedCount;

        h.Feed(2, face: false);

        Assert.Equal(TrackingStatus.NoFace, h.Controller.TrackingStatus);
        Assert.True(h.StateChangedCount > before);
    }
}
