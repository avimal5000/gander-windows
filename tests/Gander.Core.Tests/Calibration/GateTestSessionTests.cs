using Gander.Core.Calibration;
using Gander.Core.Tests.Controller;

namespace Gander.Core.Tests.Calibration;

/// <summary>Ports of the Mac GateTestSessionTests and CalibrationRunTests (timing parts).</summary>
public sealed class GateTestSessionTests
{
    private static GateTestSession Make(DisplayLayout? layout = null, int rounds = 2, double hold = 3, TestClock? clock = null)
    {
        clock ??= new TestClock();
        return new GateTestSession(layout ?? Fixtures.TwoDisplays, "Test cam", new GateTestConfiguration(rounds, hold), () => clock.Now);
    }

    [Fact]
    public void StepsAreRoundsTimesDisplaysInLayoutOrder()
    {
        var session = Make(Fixtures.ThreeDisplays, 2);
        Assert.Equal(6, session.Steps.Count);
        Assert.Equal([1, 1, 1, 2, 2, 2], session.Steps.Select(s => s.Round));
        Assert.Equal(
            [Fixtures.LeftId, Fixtures.RightId, Fixtures.FarId, Fixtures.LeftId, Fixtures.RightId, Fixtures.FarId],
            session.Steps.Select(s => s.Display.Id));
        Assert.Equal([1, 2, 3, 4, 5, 6], session.Steps.Select(s => s.Number));
        Assert.Equal([1, 2, 3, 1, 2, 3], session.Steps.Select(s => s.DisplayNumber));
    }

    [Fact]
    public void LayoutOrderIsLeftToRightEvenIfInputIsShuffled()
    {
        var session = Make(new DisplayLayout([Fixtures.Right, Fixtures.Left]), 1);
        Assert.Equal([Fixtures.LeftId, Fixtures.RightId], session.Steps.Select(s => s.Display.Id));
    }

    [Fact]
    public void StartBeginsSettlingOnTheFirstStep()
    {
        var session = Make();
        Assert.Equal(GateSessionPhase.Pending, session.Phase);
        Assert.Null(session.CurrentStep);
        session.Start();
        Assert.Equal(GateSessionPhase.Settling, session.Phase);
        Assert.Equal(Fixtures.LeftId, session.CurrentStep!.Display.Id);
        Assert.Equal(GatePhase.Settle, session.Progress!.Phase);
    }

    [Fact]
    public void SettleLastsOneAndAHalfSecondsThenHoldLastsTheConfiguredTime()
    {
        var clock = new TestClock();
        var session = Make(hold: 4, clock: clock);
        session.Start();

        clock.Set(1.49);
        session.Advance();
        Assert.Equal(GateSessionPhase.Settling, session.Phase);
        clock.Set(1.5);
        session.Advance();
        Assert.Equal(GateSessionPhase.Holding, session.Phase);
        clock.Set(5.49);
        session.Advance();
        Assert.Equal(GateSessionPhase.Holding, session.Phase);
        Assert.Equal(1, session.CurrentStep!.Number);
        clock.Set(5.5);
        session.Advance();
        Assert.Equal(GateSessionPhase.Settling, session.Phase);
        Assert.Equal(2, session.CurrentStep!.Number);
        Assert.Equal(Fixtures.RightId, session.CurrentStep.Display.Id);
    }

    [Fact]
    public void OnlyHoldSamplesAreRecordedAndTheyKeepTheirStepLabel()
    {
        var clock = new TestClock();
        var session = Make(rounds: 1, hold: 3, clock: clock);
        session.Start();

        clock.Set(0.5);
        session.Ingest(Fixtures.Sample(0.5, yaw: 99));
        clock.Set(1.4);
        session.Ingest(Fixtures.Sample(1.4, yaw: 99));
        clock.Set(1.5);
        session.Ingest(Fixtures.Sample(1.5, yaw: -20));
        clock.Set(2.5);
        session.Ingest(Fixtures.Sample(2.5, yaw: -21));
        clock.Set(4.5);
        session.Ingest(Fixtures.Sample(4.5, yaw: 99));
        clock.Set(6.0);
        session.Ingest(Fixtures.Sample(6.0, yaw: 20));
        clock.Set(9.0);
        session.Ingest(Fixtures.Sample(9.0, yaw: 99));

        Assert.Equal(GateSessionPhase.Finished, session.Phase);
        var trace = session.MakeTrace(DateTimeOffset.UnixEpoch)!;
        Assert.Equal(2, trace.Segments.Count);
        Assert.Equal(Fixtures.LeftId, trace.Segments[0].DisplayId);
        Assert.Equal(1, trace.Segments[0].Round);
        Assert.Equal([-20.0, -21.0], trace.Segments[0].Samples.Select(s => s.Pose!.Value.Yaw));
        Assert.Equal(Fixtures.RightId, trace.Segments[1].DisplayId);
        Assert.Equal([20.0], trace.Segments[1].Samples.Select(s => s.Pose!.Value.Yaw));
    }

    [Fact]
    public void SamplesWithoutAFaceAreRecordedDuringHold()
    {
        var clock = new TestClock();
        var session = Make(rounds: 1, hold: 3, clock: clock);
        session.Start();
        clock.Set(1.5);
        session.Ingest(Fixtures.Sample(1.5, face: false));
        clock.Set(2.0);
        session.Ingest(Fixtures.Sample(2.0));
        var progress = session.Progress!;
        Assert.Equal(2, progress.RecordedSamples);
        Assert.Equal(1, progress.RecordedFaces);
    }

    [Fact]
    public void AFullRunBuildsATraceWithOneSegmentPerStepInOrder()
    {
        var clock = new TestClock();
        var session = Make(rounds: 2, hold: 3, clock: clock);
        session.Start();
        var guard = 0;
        while (session.IsActive && guard++ < 1000)
        {
            clock.Advance(0.25);
            session.Ingest(Fixtures.Sample(clock.Now));
        }

        Assert.Equal(GateSessionPhase.Finished, session.Phase);
        var created = DateTimeOffset.FromUnixTimeSeconds(42);
        var trace = session.MakeTrace(created)!;
        Assert.Equal(created, trace.CreatedAt);
        Assert.Equal("Test cam", trace.CameraName);
        Assert.Equal(Fixtures.TwoDisplays, trace.Layout);
        Assert.Equal([1, 1, 2, 2], trace.Segments.Select(s => s.Round));
        Assert.Equal([Fixtures.LeftId, Fixtures.RightId, Fixtures.LeftId, Fixtures.RightId], trace.Segments.Select(s => s.DisplayId!.Value));
        Assert.All(trace.Segments, s => Assert.Equal(12, s.Samples.Count));
        Assert.Null(session.CurrentStep);
    }

    [Fact]
    public void CancelStopsRecordingAndProducesNoTrace()
    {
        var clock = new TestClock();
        var session = Make(clock: clock);
        session.Start();
        clock.Set(2);
        session.Ingest(Fixtures.Sample(2));
        session.Cancel();
        Assert.Equal(GateSessionPhase.Cancelled, session.Phase);
        Assert.Null(session.MakeTrace(DateTimeOffset.UnixEpoch));
        Assert.Null(session.Progress);

        clock.Set(3);
        session.Ingest(Fixtures.Sample(3));
        session.Advance();
        Assert.Equal(GateSessionPhase.Cancelled, session.Phase);
        Assert.Null(session.MakeTrace(DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void AFinishedSessionCannotBeCancelled()
    {
        var clock = new TestClock();
        var session = Make(Fixtures.OneDisplay, 1, 3, clock);
        session.Start();
        clock.Set(1.5);
        session.Advance();
        clock.Set(4.5);
        session.Advance();
        Assert.Equal(GateSessionPhase.Finished, session.Phase);
        session.Cancel();
        Assert.Equal(GateSessionPhase.Finished, session.Phase);
        Assert.NotNull(session.MakeTrace(DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void ProgressReportsElapsedTimeAndPosition()
    {
        var clock = new TestClock();
        var session = Make(rounds: 2, hold: 4, clock: clock);
        session.Start();
        clock.Set(0.5);
        var progress = session.Progress!;
        Assert.Equal(GatePhase.Settle, progress.Phase);
        Assert.Equal(0.5, progress.PhaseElapsed);
        Assert.Equal(1.5, progress.PhaseDuration);
        Assert.Equal(1.0, progress.Remaining);
        Assert.Equal(1, progress.StepNumber);
        Assert.Equal(4, progress.StepCount);
        Assert.Equal(1, progress.Round);
        Assert.Equal(2, progress.Rounds);
        Assert.Equal(1, progress.DisplayNumber);
        Assert.Equal(2, progress.DisplayCount);

        clock.Set(3.5);
        session.Advance();
        clock.Set(5.5);
        progress = session.Progress!;
        Assert.Equal(GatePhase.Hold, progress.Phase);
        Assert.Equal(4, progress.PhaseDuration);
        Assert.Equal(0.5, progress.Fraction);
    }

    [Fact]
    public void ALayoutWithoutDisplaysFinishesImmediatelyWithNoSegments()
    {
        var session = Make(DisplayLayout.Empty);
        session.Start();
        Assert.Equal(GateSessionPhase.Finished, session.Phase);
        Assert.Empty(session.MakeTrace(DateTimeOffset.UnixEpoch)!.Segments);
    }

    [Fact]
    public void ConfigurationIsClampedToTheAllowedRanges()
    {
        Assert.Equal(1, new GateTestConfiguration(0, 1).Rounds);
        Assert.Equal(3, new GateTestConfiguration(9, 99).Rounds);
        Assert.Equal(3, new GateTestConfiguration(2, 1).HoldDuration);
        Assert.Equal(10, new GateTestConfiguration(2, 99).HoldDuration);
        Assert.Equal(new GateTestConfiguration(2, 5), new GateTestConfiguration());
    }

    [Fact]
    public void EstimatedDurationCountsSettleAndHoldForEveryStep() =>
        Assert.Equal(4 * 6.5, new GateTestConfiguration(2, 5).EstimatedDuration(2));

    // CalibrationRunTests: timing

    [Fact]
    public void TheCalibrationIsTwoRoundsOfTwoAndAHalfSecondHolds()
    {
        var configuration = GateTestConfiguration.Calibration;
        Assert.Equal(2, configuration.Rounds);
        Assert.Equal(2.5, configuration.HoldDuration);
        Assert.Equal(1.5, GateTestConfiguration.SettleDuration);
    }

    [Fact]
    public void ThreeDisplaysTakeAboutTwentyFourSecondsAndTwoTakeSixteen()
    {
        Assert.Equal(24, GateTestConfiguration.Calibration.EstimatedDuration(3));
        Assert.Equal(16, GateTestConfiguration.Calibration.EstimatedDuration(2));
    }

    [Fact]
    public void TheGateTestWindowStillCannotHoldLessThanThreeSeconds()
    {
        Assert.Equal(3, new GateTestConfiguration(2, 2.5).HoldDuration);
        Assert.Equal(5, new GateTestConfiguration().HoldDuration);
        Assert.NotEqual(GateTestConfiguration.Calibration, new GateTestConfiguration());
    }

    [Fact]
    public void ASessionOnTheCalibrationSettingsSettlesThenHoldsForTheRightTimes()
    {
        var clock = new TestClock();
        var session = new GateTestSession(Fixtures.TwoDisplays, null, GateTestConfiguration.Calibration, () => clock.Now);
        Assert.Equal(4, session.Steps.Count);
        session.Start();
        clock.Set(1.49);
        session.Advance();
        Assert.Equal(GateSessionPhase.Settling, session.Phase);
        clock.Set(1.5);
        session.Advance();
        Assert.Equal(GateSessionPhase.Holding, session.Phase);
        Assert.Equal(2.5, session.Progress!.PhaseDuration);
        clock.Set(3.99);
        session.Advance();
        Assert.Equal(GateSessionPhase.Holding, session.Phase);
        clock.Set(4.0);
        session.Advance();
        Assert.Equal(GateSessionPhase.Settling, session.Phase);
        Assert.Equal(Fixtures.RightId, session.CurrentStep!.Display.Id);
    }
}
