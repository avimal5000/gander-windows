using Sway.Core.Controller;
using Sway.Core.Platform;
using Sway.Core.Runtime;

namespace Sway.Core.Tests.Controller;

/// <summary>Port of the Mac AppModelHoldTests ("AppModel display and Space holds"; Mac CONTRACT 6.5.5 item 1, SPEC D44).</summary>
public class HoldTests
{
    private static readonly DisplayId Left = Fixtures.LeftId;
    private static readonly DisplayId Right = Fixtures.RightId;

    private static async Task<Harness> TrackingHarness()
    {
        var h = Harness.Make(calibrated: true);
        await h.StartTracking();
        h.Drain();
        return h;
    }

    // A decider that fires at 1.4 s and keeps asking to switch. It ignores the lock on purpose, so a test sees that the
    // controller, not the decider, refuses the request.
    private static void ScriptLook(Harness h, DisplayId target) => TrackingTests.ScriptLook(h, target, dwellFrom: 99, fireAt: 1.4, ignoresLock: true);

    /// <summary>The same two displays, the right one smaller: a new layout the calibration still fits.</summary>
    private static DisplayLayout Rearranged =>
        new([Fixtures.Left, new DisplayInfo(Right, 12, "Right", new DisplayRect(1920, 0, 1280, 720), false, false)]);

    private static void ScreenParametersChanged(Harness h)
    {
        h.Displays.Notify();
        h.Drain();
    }

    // MARK: Displays changing

    [Fact]
    public async Task AScreenParameterChangeHoldsSwitchingAndLocksTheDecider()
    {
        using var h = await TrackingHarness();
        h.Focus.Result = TrackingTests.FocusedRight();
        ScriptLook(h, Right);

        ScreenParametersChanged(h);
        Assert.Equal(TrackingHold.DisplaysChanging, h.Controller.TrackingHold);
        h.FeedBurst(1.0, 1.4, 1.6);

        Assert.All(h.Engine.LastRecorder!.Contexts, c => Assert.True(c.IsFocusLocked));
        Assert.Empty(h.Focus.FocusCalls); // No switch while the displays change.
        Assert.Empty(h.Controller.SwitchLog);
    }

    [Fact]
    public async Task TheHoldEndsWhenTheNewLayoutIsApplied()
    {
        using var h = await TrackingHarness();
        h.Focus.Result = TrackingTests.FocusedRight();
        ScriptLook(h, Right);
        ScreenParametersChanged(h);
        Assert.Equal(TrackingHold.DisplaysChanging, h.Controller.TrackingHold);

        h.Displays.Change(Rearranged);
        h.Drain();

        Assert.Null(h.Controller.TrackingHold);
        Assert.False(h.Scheduler.HasPending); // The wait is cancelled with the hold.
        h.Feed(1.4);
        Assert.Equal([Right], h.Focus.FocusCalls); // Switching is back.
    }

    [Fact]
    public async Task TheHoldEndsAfterThreeQuartersOfASecondWhenNoLayoutArrives()
    {
        using var h = await TrackingHarness();

        ScreenParametersChanged(h);
        Assert.Equal([TrackingHolds.DisplaysChangingTimeout], h.Scheduler.PendingDelays);
        Assert.Equal(0.75, TrackingHolds.DisplaysChangingTimeout);

        h.Advance(0.74);
        Assert.Equal(TrackingHold.DisplaysChanging, h.Controller.TrackingHold);
        h.Advance(0.01);
        Assert.Null(h.Controller.TrackingHold);
    }

    [Fact]
    public async Task ALayoutThatDidNotChangeDoesNotEndTheHold()
    {
        using var h = await TrackingHarness();

        ScreenParametersChanged(h);
        ScreenParametersChanged(h); // The catalog still reports the same layout.
        Assert.Equal(TrackingHold.DisplaysChanging, h.Controller.TrackingHold); // Nothing new is known yet.

        h.Advance(0.75);
        Assert.Null(h.Controller.TrackingHold);
    }

    [Fact]
    public async Task AnotherNotificationRestartsTheWait()
    {
        using var h = await TrackingHarness();

        ScreenParametersChanged(h);
        h.Advance(0.5);
        ScreenParametersChanged(h);
        h.Advance(0.5);
        Assert.Equal(TrackingHold.DisplaysChanging, h.Controller.TrackingHold); // 0.5 s into the second wait.

        h.Advance(0.25);
        Assert.Null(h.Controller.TrackingHold);
    }

    [Fact]
    public async Task StartingTheHoldResetsTheLoopAndDropsASwitchInFlight()
    {
        using var h = await TrackingHarness();
        h.Focus.HoldFocusRequests();
        ScriptLook(h, Right);
        h.Feed(1.4);
        Assert.True(h.Focus.IsHoldingFocus);
        var recorder = h.Engine.LastRecorder!;
        var resets = recorder.ResetCount;
        Assert.True(h.Controller.IsSwitching);

        ScreenParametersChanged(h);

        Assert.False(h.Controller.IsSwitching);
        Assert.Null(h.Controller.TrackingFocus);
        Assert.Null(h.Controller.LastDecision);
        Assert.Equal(resets + 1, recorder.ResetCount); // The dwell and cooldown are gone, as after a layout change.

        h.Focus.Result = TrackingTests.FocusedRight();
        h.Focus.Release();
        h.Drain();
        Assert.Empty(h.Controller.SwitchLog); // A result from before the change says nothing about the new layout.
    }

    [Fact]
    public async Task FocusIsPolledOnceTheHoldEnds()
    {
        using var h = await TrackingHarness();
        h.Focus.FocusedDisplay = Right;
        ScreenParametersChanged(h);
        var calls = h.Focus.FocusedDisplayCalls;
        Assert.Null(h.Controller.TrackingFocus); // Unknown while the layout settles.

        h.Advance(0.75);

        Assert.Equal(calls + 1, h.Focus.FocusedDisplayCalls);
        Assert.Equal(Right, h.Controller.TrackingFocus);
    }

    [Fact]
    public async Task FocusIsPolledWhenTheLayoutEndsTheHoldToo()
    {
        using var h = await TrackingHarness();
        h.Focus.FocusedDisplay = Left;
        ScreenParametersChanged(h);
        var calls = h.Focus.FocusedDisplayCalls;

        h.Displays.Change(Rearranged);
        h.Drain();

        Assert.Equal(calls + 1, h.Focus.FocusedDisplayCalls); // One poll, not one for the layout and one for the hold.
        Assert.Equal(Left, h.Controller.TrackingFocus);
    }

    // MARK: The cap (Mac CONTRACT 6.5.5 item 1, cap and log)

    [Fact]
    public async Task RepeatedNotificationsEveryHalfSecondEndTheHoldThreeSecondsAfterTheFirst()
    {
        using var h = await TrackingHarness();
        var recorder = h.Engine.LastRecorder!;
        var resets = recorder.ResetCount;
        Assert.Equal(3, TrackingHolds.DisplaysChangingCap);

        for (var index = 0; index < 6; index++)
        {
            ScreenParametersChanged(h); // At 0, 0.5, ... 2.5 s.
            Assert.Equal(TrackingHold.DisplaysChanging, h.Controller.TrackingHold);
            if (index < 5)
            {
                h.Advance(0.5);
            }
        }

        Assert.Equal(resets + 6, recorder.ResetCount); // Every notification inside the cap still resets the loop.
        Assert.Equal([0.5], h.Scheduler.PendingDelays); // The last wait is cut to what is left of the 3 s.
        h.Advance(0.49);
        Assert.Equal(TrackingHold.DisplaysChanging, h.Controller.TrackingHold);
        h.Advance(0.01);
        Assert.Null(h.Controller.TrackingHold); // Three seconds after the first notification.
    }

    [Fact]
    public async Task AfterTheCapTheSameChatterNeitherHoldsNorResetsAndSwitchingWorks()
    {
        using var h = await TrackingHarness();
        h.Focus.Result = TrackingTests.FocusedRight();
        ScriptLook(h, Right);
        for (var i = 0; i < 6; i++)
        {
            ScreenParametersChanged(h);
            h.Advance(0.5);
        }

        Assert.Null(h.Controller.TrackingHold); // Capped at 3 s.
        var recorder = h.Engine.LastRecorder!;
        var resets = recorder.ResetCount;
        for (var i = 0; i < 6; i++)
        {
            // The display keeps posting every 0.5 s with the same layout.
            ScreenParametersChanged(h);
            Assert.Null(h.Controller.TrackingHold);
            h.Advance(0.5);
        }

        Assert.Equal(resets, recorder.ResetCount); // Ignored, not reset.
        Assert.False(h.Scheduler.HasPending);
        h.Feed(1.4);
        Assert.Equal([Right], h.Focus.FocusCalls); // Switching works while the display chatters.
    }

    [Fact]
    public async Task APauseInTheChatterLetsTheNextNotificationHoldAgain()
    {
        using var h = await TrackingHarness();
        for (var i = 0; i < 6; i++)
        {
            ScreenParametersChanged(h);
            h.Advance(0.5);
        }

        ScreenParametersChanged(h);
        Assert.Null(h.Controller.TrackingHold); // Still the same chatter.
        h.Advance(TrackingHolds.DisplaysChangingTimeout);
        ScreenParametersChanged(h);
        Assert.Equal(TrackingHold.DisplaysChanging, h.Controller.TrackingHold); // A real change after a quiet moment.
    }

    [Fact]
    public async Task AHoldThatEndedByItsLayoutOrItsTimeoutDoesNotIgnoreTheNextNotification()
    {
        using var h = await TrackingHarness();
        ScreenParametersChanged(h);
        h.Displays.Change(Rearranged);
        h.Drain();
        Assert.Null(h.Controller.TrackingHold);

        h.Advance(0.1);
        ScreenParametersChanged(h);
        Assert.Equal(TrackingHold.DisplaysChanging, h.Controller.TrackingHold);
        h.Advance(TrackingHolds.DisplaysChangingTimeout);
        Assert.Null(h.Controller.TrackingHold);

        h.Advance(0.1);
        ScreenParametersChanged(h);
        Assert.Equal(TrackingHold.DisplaysChanging, h.Controller.TrackingHold);
    }

    [Fact]
    public async Task TheHoldIsPolledAtItsCapToo()
    {
        using var h = await TrackingHarness();
        h.Focus.FocusedDisplay = Right;
        for (var i = 0; i < 6; i++)
        {
            ScreenParametersChanged(h);
            h.Advance(0.5);
        }

        Assert.Equal(Right, h.Controller.TrackingFocus); // Focus is read again when the cap ends the hold.
    }

    [Fact]
    public async Task TheHoldsAreLoggedWhenTheyStartAndEnd()
    {
        using var h = await TrackingHarness();

        ScreenParametersChanged(h);
        h.Advance(0.75);
        h.Controller.ActiveDesktopChanged();
        h.Advance(0.6);

        Assert.Contains("Display hold started", h.Controller.LogLines);
        Assert.Contains("Display hold ended by timeout after 0.75 s", h.Controller.LogLines);
        Assert.Contains("Space hold started", h.Controller.LogLines);
        Assert.Contains("Space hold ended by timeout after 0.60 s", h.Controller.LogLines);
    }

    [Fact]
    public async Task ALayoutChangeWithoutANotificationLeavesNoHold()
    {
        using var h = await TrackingHarness();

        h.Displays.Change(Rearranged);
        h.Drain();

        Assert.Null(h.Controller.TrackingHold);
        Assert.False(h.Scheduler.HasPending);
    }

    [Fact]
    public async Task TheHoldsRankInThePublishedOrder()
    {
        using var h = await TrackingHarness();
        h.Focus.HoldFocusRequests();
        ScriptLook(h, Right);
        h.Feed(1.4);
        Assert.Equal(TrackingHold.Switching, h.Controller.TrackingHold);

        h.Controller.ActiveDesktopChanged();
        Assert.Equal(TrackingHold.SpaceChanging, h.Controller.TrackingHold);
        ScreenParametersChanged(h);
        Assert.Equal(TrackingHold.DisplaysChanging, h.Controller.TrackingHold);
        h.Controller.SetSwayInFront(true);
        Assert.Equal(TrackingHold.SwayInFront, h.Controller.TrackingHold);
        h.Controller.SetFocusLock(true);
        Assert.Equal(TrackingHold.FocusLock, h.Controller.TrackingHold);
    }

    // MARK: Virtual desktops changing

    [Fact]
    public async Task ADesktopChangeHoldsForSixTenthsOfASecond()
    {
        using var h = await TrackingHarness();
        h.Focus.Result = TrackingTests.FocusedRight();
        ScriptLook(h, Right);

        h.Controller.ActiveDesktopChanged();
        Assert.Equal(TrackingHold.SpaceChanging, h.Controller.TrackingHold);
        Assert.Equal([TrackingHolds.SpaceChangingDuration], h.Scheduler.PendingDelays);
        Assert.Equal(0.6, TrackingHolds.SpaceChangingDuration);
        h.FeedBurst(1.0, 1.4, 1.6);

        Assert.Empty(h.Focus.FocusCalls); // No switch while the desktop settles.
        Assert.All(h.Engine.LastRecorder!.Contexts, c => Assert.True(c.IsFocusLocked));
        h.Advance(0.59);
        Assert.Equal(TrackingHold.SpaceChanging, h.Controller.TrackingHold);
        h.Advance(0.01);
        Assert.Null(h.Controller.TrackingHold);
        h.Feed(2.0);
        Assert.Equal([Right], h.Focus.FocusCalls); // Switching is back.
    }

    [Fact]
    public async Task TheDesktopHoldEndsWithOnePollOfTheFocusedDisplay()
    {
        using var h = await TrackingHarness();
        h.Focus.FocusedDisplay = Right;
        h.Controller.ActiveDesktopChanged();
        var calls = h.Focus.FocusedDisplayCalls;

        h.Advance(0.6);

        Assert.Equal(calls + 1, h.Focus.FocusedDisplayCalls);
        Assert.Equal(Right, h.Controller.TrackingFocus);
    }

    [Fact]
    public async Task APollThatBeganBeforeTheDesktopChangeIsDropped()
    {
        using var h = await TrackingHarness();
        h.Focus.FocusedDisplay = Right;
        h.Focus.HoldPolls();
        h.Controller.TrackingTick(); // Starts a poll that will answer "right".

        h.Controller.ActiveDesktopChanged();
        h.Focus.ReleasePoll();
        h.Drain();

        Assert.Null(h.Controller.TrackingFocus); // It may have seen the old desktop.
    }

    [Fact]
    public async Task AnotherDesktopChangeRestartsTheHold()
    {
        using var h = await TrackingHarness();

        h.Controller.ActiveDesktopChanged();
        h.Advance(0.4);
        h.Controller.ActiveDesktopChanged();
        h.Advance(0.4);
        Assert.Equal(TrackingHold.SpaceChanging, h.Controller.TrackingHold);

        h.Advance(0.2);
        Assert.Null(h.Controller.TrackingHold);
    }

    [Fact]
    public async Task ADesktopChangeDoesNotResetTheLoopOrDropASwitchInFlight()
    {
        using var h = await TrackingHarness();
        h.Focus.HoldFocusRequests();
        ScriptLook(h, Right);
        h.Feed(1.4);
        Assert.True(h.Focus.IsHoldingFocus);
        h.Focus.Result = TrackingTests.FocusedRight();

        h.Controller.ActiveDesktopChanged();
        h.Focus.Release();
        h.Drain();

        Assert.Single(h.Controller.SwitchLog);
        Assert.Equal(Right, h.Controller.TrackingFocus);
    }

    [Fact]
    public async Task ADesktopChangeDuringAnInFlightSwitchLocksTheDeciderButLetsTheSwitchFinish()
    {
        // Pins what happens today: the desktop hold ranks above Switching, so the decider is told it is locked (and a
        // real decider resets on that), while the controller leaves the loop and the request alone.
        using var h = await TrackingHarness();
        h.Focus.HoldFocusRequests();
        ScriptLook(h, Right);
        h.Feed(1.4);
        Assert.True(h.Focus.IsHoldingFocus);
        var recorder = h.Engine.LastRecorder!;
        var resets = recorder.ResetCount;
        Assert.Equal(TrackingHold.Switching, h.Controller.TrackingHold);

        h.Controller.ActiveDesktopChanged();
        h.Feed(1.5);
        Assert.Equal(TrackingHold.SpaceChanging, h.Controller.TrackingHold); // Ranks above Switching.
        Assert.Equal(new SwitchContext(Right, true, null), recorder.Contexts.Last()); // Locked, with the request in flight still believed focused.
        Assert.Equal(resets, recorder.ResetCount); // The controller never resets the decider for a desktop change.
        Assert.True(h.Controller.IsSwitching); // The loop is untouched.
        Assert.Equal([Right], h.Focus.FocusCalls); // The decider's repeated request is refused.

        h.Focus.Result = TrackingTests.FocusedRight();
        h.Focus.Release();
        h.Drain();
        Assert.Single(h.Controller.SwitchLog); // Its result still counts.
        Assert.Equal(Right, h.Controller.TrackingFocus);
        Assert.Equal(TrackingHold.SpaceChanging, h.Controller.TrackingHold); // Still settling after the switch.

        h.Focus.FocusedDisplay = Right; // What the poll at the end of the hold reads.
        h.Advance(0.6);
        Assert.Null(h.Controller.TrackingHold);
        h.Feed(1.6);
        Assert.Equal(new SwitchContext(Right, false, null), recorder.Contexts.Last());
    }

    // MARK: Both

    [Fact]
    public async Task BothHoldsAreInvisibleInTheMenu()
    {
        using var h = await TrackingHarness();
        h.Feed(1);
        var status = h.Controller.TrackingStatus;
        var line = h.Controller.StatusLine;
        var icon = h.Controller.TrayIcon;
        var changes = h.StateChangedCount;
        Assert.Equal(TrackingStatus.Tracking, status);

        ScreenParametersChanged(h);
        Assert.Equal(status, h.Controller.TrackingStatus);
        Assert.Equal(line, h.Controller.StatusLine);
        Assert.Equal(icon, h.Controller.TrayIcon);

        h.Controller.ActiveDesktopChanged();
        Assert.Equal(status, h.Controller.TrackingStatus);
        Assert.Equal(line, h.Controller.StatusLine);
        Assert.Equal(changes, h.StateChangedCount);
    }

    [Fact]
    public async Task QuittingDropsTheHoldsAndTheirTimers()
    {
        using var h = await TrackingHarness();
        ScreenParametersChanged(h);
        h.Controller.ActiveDesktopChanged();
        Assert.True(h.Scheduler.HasPending);

        await h.Controller.ShutdownAsync();
        h.Drain();

        Assert.False(h.Scheduler.HasPending);
        Assert.Null(h.Controller.TrackingHold);
    }
}
