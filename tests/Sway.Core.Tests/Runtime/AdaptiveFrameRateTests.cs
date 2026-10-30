using Sway.Core.Runtime;

namespace Sway.Core.Tests.Runtime;

/// <summary>
/// The pure frame-rate rule of CONTRACT 6.2.1. The expected numbers are written out here (1/15 s, 1/10 s, 1/5 s, 1/2 s,
/// a 2 s absence, a 4 ms tolerance) and do not read the production constants, so a wrong constant fails.
/// </summary>
public class AdaptiveFrameRateTests
{
    private const double Tolerance = 0.004;
    private const double Epsilon = 1e-9;

    public sealed record Row(PoseDemand Demand, bool Constrained, bool FaceRecent, double Interval)
    {
        public override string ToString() =>
            $"{Demand}, {(Constrained ? "power constrained" : "no constraint")}, "
            + $"{(FaceRecent ? "face within 2 s" : "no face for 2 s")}";
    }

    private static readonly Row[] Table =
    {
        new(PoseDemand.Full, false, true, 1.0 / 15),
        new(PoseDemand.Full, false, false, 1.0 / 15),
        new(PoseDemand.Full, true, true, 1.0 / 15),
        new(PoseDemand.Full, true, false, 1.0 / 15),
        new(PoseDemand.Adaptive, false, true, 1.0 / 15),
        new(PoseDemand.Adaptive, false, false, 1.0 / 5),
        new(PoseDemand.Adaptive, true, true, 1.0 / 10),
        new(PoseDemand.Adaptive, true, false, 1.0 / 2),
        new(PoseDemand.Reduced, false, true, 1.0 / 5),
        new(PoseDemand.Reduced, false, false, 1.0 / 5),
        new(PoseDemand.Reduced, true, true, 1.0 / 2),
        new(PoseDemand.Reduced, true, false, 1.0 / 2),
    };

    public static TheoryData<Row> Rows => new(Table);

    public static TheoryData<Row> AbsentRows => new(Table.Where(r => !r.FaceRecent));

    public sealed record CameraCase(double CameraFps, PoseDemand Demand, bool Constrained, int ExpectedFps)
    {
        public override string ToString() =>
            $"{CameraFps} fps camera, {Demand}{(Constrained ? ", power constrained" : string.Empty)} gives {ExpectedFps} fps";
    }

    public static TheoryData<CameraCase> CameraCases => new(
        // The hard cap: 15 fps from every camera that is faster.
        new CameraCase(15, PoseDemand.Full, false, 15),
        new CameraCase(20, PoseDemand.Full, false, 15),
        new CameraCase(24, PoseDemand.Full, false, 15),
        new CameraCase(30, PoseDemand.Full, false, 15),
        new CameraCase(60, PoseDemand.Full, false, 15),
        new CameraCase(120, PoseDemand.Full, false, 15),
        new CameraCase(24, PoseDemand.Adaptive, false, 15),
        // 1/10 s.
        new CameraCase(15, PoseDemand.Adaptive, true, 10),
        new CameraCase(20, PoseDemand.Adaptive, true, 10),
        new CameraCase(24, PoseDemand.Adaptive, true, 10),
        new CameraCase(30, PoseDemand.Adaptive, true, 10),
        // 1/5 s and 1/2 s.
        new CameraCase(15, PoseDemand.Reduced, false, 5),
        new CameraCase(24, PoseDemand.Reduced, false, 5),
        new CameraCase(15, PoseDemand.Reduced, true, 2),
        new CameraCase(24, PoseDemand.Reduced, true, 2),
        new CameraCase(30, PoseDemand.Reduced, true, 2));

    /// <summary>A rate in the state of <paramref name="row"/>, whose first frame was processed at 100 and had a face.</summary>
    private static AdaptiveFrameRate MakeRate(Row row)
    {
        var rate = new AdaptiveFrameRate();
        rate.SetDemand(row.Demand);
        rate.SetPowerConstrained(row.Constrained);
        ExpectProcessed(rate, 100);
        rate.Record(true, 100);
        return rate;
    }

    private static void ExpectProcessed(AdaptiveFrameRate rate, double t, bool expected = true) =>
        Assert.Equal(expected, rate.ShouldProcess(t));

    private static double TimeFor(Row row) => row.FaceRecent ? 101 : 105;

    private static List<double> Simulate(
        AdaptiveFrameRate rate, double start, double seconds, double fps, Func<double, bool>? face = null)
    {
        var processed = new List<double>();
        var count = (int)Math.Round(seconds * fps, MidpointRounding.ToEven);
        for (var index = 0; index <= count; index++)
        {
            var t = start + (index / fps);
            if (rate.ShouldProcess(t))
            {
                rate.Record(face?.Invoke(t) ?? false, t);
                processed.Add(t);
            }
        }

        return processed;
    }

    private static List<double> Gaps(IReadOnlyList<double> times) =>
        times.Zip(times.Skip(1), (a, b) => b - a).ToList();

    // Start and the table.

    [Theory]
    [MemberData(nameof(Rows))]
    public void FirstFrameIsAlwaysProcessed(Row row)
    {
        var rate = new AdaptiveFrameRate();
        rate.SetDemand(row.Demand);
        rate.SetPowerConstrained(row.Constrained);

        ExpectProcessed(rate, 12_345.678);
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public void IntervalTable(Row row)
    {
        var rate = MakeRate(row);

        Assert.True(Math.Abs(rate.MinimumInterval(TimeFor(row)) - row.Interval) < Epsilon);
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public void Over10SecondsOf60FpsInputEachRowProcessesAtItsRateAndNoFaster(Row row)
    {
        var rate = MakeRate(row);
        var start = row.FaceRecent ? 100.0 : 105.0;
        var processed = Simulate(rate, start, 10, 60, _ => row.FaceRecent);

        var framesPerInterval = Math.Round(row.Interval * 60, MidpointRounding.ToEven);
        var expected = (int)Math.Floor(600 / framesPerInterval) + (row.FaceRecent ? 0 : 1);
        Assert.Equal(expected, processed.Count);
        var gaps = Gaps(processed);
        var steady = row.FaceRecent ? gaps : gaps.Skip(1).ToList();
        foreach (var gap in steady)
        {
            Assert.True(Math.Abs(gap - row.Interval) < 1e-6);
        }

        if (!row.FaceRecent && gaps.Count > 0)
        {
            Assert.True(Math.Abs(gaps[0] - (row.Interval / 2)) < 1e-6);
        }
    }

    [Fact]
    public void FullDemandIgnoresPowerAndFaces()
    {
        var rate = new AdaptiveFrameRate();
        rate.SetPowerConstrained(true);
        var processed = Simulate(rate, 0, 20, 15);

        Assert.Equal(301, processed.Count);
    }

    // Boundaries and tolerance.

    [Theory]
    [MemberData(nameof(AbsentRows))]
    public void AFrameIsProcessedFromIntervalMinus4MsAfterItsSlotNotBefore(Row row)
    {
        var interval = row.Interval;
        var dueTime = 10 + (1.5 * interval);

        AdaptiveFrameRate Prepared()
        {
            var rate = new AdaptiveFrameRate();
            rate.SetDemand(row.Demand);
            rate.SetPowerConstrained(row.Constrained);
            foreach (var t in new[] { 0, 10, 10 + (interval / 2) })
            {
                rate.ShouldProcess(t);
                rate.Record(false, t);
            }

            return rate;
        }

        ExpectProcessed(Prepared(), dueTime - Tolerance - 1e-6, false);
        ExpectProcessed(Prepared(), dueTime - Tolerance + 1e-6);
    }

    [Fact]
    public void ARejectedFrameDoesNotMoveTheInterval()
    {
        var rate = new AdaptiveFrameRate();
        ExpectProcessed(rate, 0);
        ExpectProcessed(rate, 0.03, false);
        ExpectProcessed(rate, 0.0667);
    }

    [Fact]
    public void AProcessedFrameAdvancesTheSlotByOneIntervalNotToItsOwnTime()
    {
        var rate = new AdaptiveFrameRate();
        ExpectProcessed(rate, 0);
        ExpectProcessed(rate, 0.0627);
        ExpectProcessed(rate, 0.1254, false);
        ExpectProcessed(rate, (2.0 / 15) - 0.004 - 1e-6, false);
        ExpectProcessed(rate, (2.0 / 15) - 0.004 + 1e-6);
    }

    [Fact]
    public void AFrameFarBehindItsSlotReAnchorsHalfAnIntervalBehindTheFrame()
    {
        var rate = new AdaptiveFrameRate();
        ExpectProcessed(rate, 0);
        ExpectProcessed(rate, 5);
        ExpectProcessed(rate, 5 + (1.0 / 30) - 0.004 - 1e-6, false);
        ExpectProcessed(rate, 5 + (1.0 / 30) - 0.004 + 1e-6);
    }

    [Fact]
    public void AFrameWithTheSameTimestampIsDropped()
    {
        var rate = new AdaptiveFrameRate();
        ExpectProcessed(rate, 5);
        ExpectProcessed(rate, 5, false);
    }

    [Fact]
    public void TimestampJitterOfPlusOrMinus5MsDropsNoFrameOfA15FpsCamera()
    {
        var jitter = new[] { -0.005, 0.005, 0.0, -0.002, 0.005, -0.004, 0.003, 0.005, -0.005, 0.001, 0.005, -0.003 };
        var rate = new AdaptiveFrameRate();
        var processed = 0;
        for (var index = 0; index < 300; index++)
        {
            if (rate.ShouldProcess(50 + (index / 15.0) + jitter[index % jitter.Length]))
            {
                processed++;
            }
        }

        Assert.Equal(300, processed);
    }

    [Fact]
    public void AlternatingJitterStartingLateLosesAtMostTheSecondFrame()
    {
        var rate = new AdaptiveFrameRate();
        var dropped = new List<int>();
        for (var index = 0; index < 300; index++)
        {
            var jitter = index % 2 == 0 ? 0.005 : -0.005;
            if (!rate.ShouldProcess(50 + (index / 15.0) + jitter))
            {
                dropped.Add(index);
            }
        }

        Assert.Equal(new[] { 1 }, dropped);
    }

    [Theory]
    [MemberData(nameof(CameraCases))]
    public void EveryCameraRateGetsExactlyTheTablesRateOver10Seconds(CameraCase scenario)
    {
        var rate = new AdaptiveFrameRate();
        rate.SetDemand(scenario.Demand);
        rate.SetPowerConstrained(scenario.Constrained);
        var processed = Simulate(rate, 0, 10, scenario.CameraFps, _ => true);

        Assert.Equal(10 * scenario.ExpectedFps, processed.Count(t => t < 10));
        for (var index = 0; index < processed.Count; index++)
        {
            var time = processed[index];
            var inWindow = processed.Skip(index).TakeWhile(t => t < time + 1 - 1e-6).Count();
            Assert.True(inWindow <= scenario.ExpectedFps + 1);
        }
    }

    [Fact]
    public void A15FpsCameraAt1Over10AlternatesGapsOfOneAndTwoFrames()
    {
        var rate = new AdaptiveFrameRate();
        rate.SetDemand(PoseDemand.Adaptive);
        rate.SetPowerConstrained(true);
        var processed = Simulate(rate, 0, 10, 15, _ => true);
        var frameGaps = Gaps(processed).Select(g => (int)Math.Round(g * 15, MidpointRounding.ToEven)).ToList();

        Assert.Equal(new HashSet<int> { 1, 2 }, frameGaps.ToHashSet());
        Assert.Equal(new[] { 2, 1, 2, 1 }, frameGaps.Take(4));
        foreach (var (first, second) in frameGaps.Zip(frameGaps.Skip(1)))
        {
            Assert.True(((double)(first + second) / 15) <= 0.2 + 1e-9);
        }
    }

    [Fact]
    public void A30FpsCameraGivesEverySecondFrameAt15Fps()
    {
        var rate = new AdaptiveFrameRate();
        var processed = Simulate(rate, 0, 10, 30);

        Assert.Equal(151, processed.Count);
        foreach (var gap in Gaps(processed))
        {
            Assert.True(Math.Abs(gap - (2.0 / 30)) < 1e-6);
        }
    }

    // Backwards time.

    [Fact]
    public void ATimestampBeforeTheLastProcessedOneIsProcessedAndRestartsTheInterval()
    {
        var rate = new AdaptiveFrameRate();
        ExpectProcessed(rate, 100);
        ExpectProcessed(rate, 99.9);
        ExpectProcessed(rate, 99.95, false);
        ExpectProcessed(rate, 99.9 + (1.0 / 15));
    }

    [Fact]
    public void ABackwardsJumpPullsAFutureFaceTimeBack()
    {
        var rate = new AdaptiveFrameRate();
        rate.SetDemand(PoseDemand.Adaptive);
        ExpectProcessed(rate, 1000);
        rate.Record(true, 1000);
        ExpectProcessed(rate, 10);
        ExpectProcessed(rate, 12.9);
        rate.Record(false, 12.9);

        ExpectProcessed(rate, 12.95, false);
        ExpectProcessed(rate, 13.0);
    }

    [Fact]
    public void ABackwardsJumpKeepsAnOldFaceTime()
    {
        var rate = new AdaptiveFrameRate();
        rate.SetDemand(PoseDemand.Adaptive);
        ExpectProcessed(rate, 10);
        rate.Record(true, 10);
        ExpectProcessed(rate, 100);
        rate.Record(false, 100);
        ExpectProcessed(rate, 50);
        rate.Record(false, 50);

        Assert.True(Math.Abs(rate.MinimumInterval(50.05) - 0.2) < Epsilon);
    }

    // The 2 s absence boundary.

    [Fact]
    public void AFaceIsRecentUpToButNotIncludingTwoSeconds()
    {
        var rate = new AdaptiveFrameRate();
        rate.SetDemand(PoseDemand.Adaptive);
        ExpectProcessed(rate, 100);
        rate.Record(true, 100);

        Assert.True(Math.Abs(rate.MinimumInterval(101.9921875) - (1.0 / 15)) < Epsilon);
        Assert.True(Math.Abs(rate.MinimumInterval(102) - (1.0 / 5)) < Epsilon);
        Assert.True(Math.Abs(rate.MinimumInterval(150) - (1.0 / 5)) < Epsilon);
    }

    [Fact]
    public void TheRateDropsToFiveFpsOnlyAfterTwoSecondsWithoutAFace()
    {
        var rate = new AdaptiveFrameRate();
        rate.SetDemand(PoseDemand.Adaptive);
        rate.ShouldProcess(100);
        rate.Record(true, 100);
        var processed = Simulate(rate, 100 + (1.0 / 15), 6, 15);

        Assert.Equal(28, processed.Count(t => t < 101.9));
        var afterwards = processed.Where(t => t > 102.2).ToList();
        foreach (var gap in Gaps(afterwards))
        {
            Assert.True(Math.Abs(gap - 0.2) < 1e-6);
        }

        Assert.True(afterwards.Count > 15);
    }

    [Fact]
    public void AFrameWithoutAFaceNeverRefreshesTheFaceTime()
    {
        var rate = new AdaptiveFrameRate();
        rate.SetDemand(PoseDemand.Adaptive);
        ExpectProcessed(rate, 100);
        rate.Record(true, 100);
        for (var index = 1; index <= 20; index++)
        {
            var t = 100 + (index * 0.1);
            if (rate.ShouldProcess(t))
            {
                rate.Record(false, t);
            }
        }

        Assert.True(Math.Abs(rate.MinimumInterval(102) - (1.0 / 5)) < Epsilon);
    }

    // Face return.

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AProcessedFrameWithAFaceMakesTheNextFrameEligibleAtTheActiveInterval(bool constrained)
    {
        var active = constrained ? 0.1 : 1.0 / 15;

        AdaptiveFrameRate Throttled()
        {
            var rate = new AdaptiveFrameRate();
            rate.SetDemand(PoseDemand.Adaptive);
            rate.SetPowerConstrained(constrained);
            rate.ShouldProcess(100);
            rate.Record(true, 100);
            rate.ShouldProcess(120);
            return rate;
        }

        var withFace = Throttled();
        withFace.Record(true, 120);
        ExpectProcessed(withFace, 120 + active);

        var withoutFace = Throttled();
        withoutFace.Record(false, 120);
        ExpectProcessed(withoutFace, 120 + active, false);
    }

    [Fact]
    public void AReturningFaceGetsEveryFrameAgain()
    {
        var rate = new AdaptiveFrameRate();
        rate.SetDemand(PoseDemand.Adaptive);
        Simulate(rate, 0, 10, 15);
        var processed = Simulate(rate, 10 + (1.0 / 15), 3, 15, t => t >= 10.5);

        var faceFrames = processed.Where(t => t >= 10.5).ToList();
        Assert.NotEmpty(faceFrames);
        Assert.True(faceFrames.Count >= 37);
        foreach (var gap in Gaps(faceFrames))
        {
            Assert.True(Math.Abs(gap - (1.0 / 15)) < 1e-6);
        }
    }

    // Reset.

    [Fact]
    public void ResetProcessesTheNextFrameAndForgetsOldTimes()
    {
        var rate = new AdaptiveFrameRate();
        rate.SetDemand(PoseDemand.Adaptive);
        ExpectProcessed(rate, 100);
        rate.Record(true, 100);
        rate.Reset();

        ExpectProcessed(rate, 100.001);
        ExpectProcessed(rate, 100.002, false);
    }

    [Fact]
    public void ResetPutsTheDemandBackToFullAndKeepsThePowerState()
    {
        var rate = new AdaptiveFrameRate();
        rate.SetDemand(PoseDemand.Reduced);
        rate.SetPowerConstrained(true);
        rate.Reset();

        Assert.Equal(PoseDemand.Full, rate.Demand);
        Assert.True(rate.IsPowerConstrained);
        Assert.True(Math.Abs(rate.MinimumInterval(5) - (1.0 / 15)) < Epsilon);
    }

    [Fact]
    public void AStartCountsAsAFaceJustSeen()
    {
        var rate = new AdaptiveFrameRate();
        rate.SetDemand(PoseDemand.Adaptive);
        rate.ShouldProcess(100);
        rate.Record(true, 100);
        rate.Reset();
        rate.SetDemand(PoseDemand.Adaptive);

        ExpectProcessed(rate, 500);
        rate.Record(false, 500);
        Assert.True(Math.Abs(rate.MinimumInterval(501.9921875) - (1.0 / 15)) < Epsilon);
        Assert.True(Math.Abs(rate.MinimumInterval(502) - (1.0 / 5)) < Epsilon);
    }

    [Fact]
    public void AResetRateEqualsAFreshOne()
    {
        var rate = new AdaptiveFrameRate();
        Assert.Equal(new AdaptiveFrameRate(), rate);
        rate.ShouldProcess(3);
        rate.Record(true, 3);
        rate.SetDemand(PoseDemand.Reduced);
        Assert.NotEqual(new AdaptiveFrameRate(), rate);

        rate.Reset();
        Assert.Equal(new AdaptiveFrameRate(), rate);
    }

    // Changes in the middle of a stream.

    [Fact]
    public void RaisingTheDemandTakesEffectOnTheNextFrame()
    {
        var rate = new AdaptiveFrameRate();
        rate.SetDemand(PoseDemand.Reduced);
        ExpectProcessed(rate, 100);
        ExpectProcessed(rate, 100.07, false);

        rate.SetDemand(PoseDemand.Full);
        ExpectProcessed(rate, 100.08);
        ExpectProcessed(rate, 100.08 + (1.0 / 15));
    }

    [Fact]
    public void LoweringTheDemandTakesEffectOnTheNextFrame()
    {
        var rate = new AdaptiveFrameRate();
        ExpectProcessed(rate, 100);
        ExpectProcessed(rate, 100 + (1.0 / 15));

        rate.SetDemand(PoseDemand.Reduced);
        ExpectProcessed(rate, 100 + (2.0 / 15), false);
        ExpectProcessed(rate, 100 + (3.0 / 15) - 0.001, false);
        ExpectProcessed(rate, 100 + (1.0 / 15) + 0.2);
    }

    [Fact]
    public void AdaptiveStartsAtFullRateBecauseTheStartCountsAsAFace()
    {
        var rate = new AdaptiveFrameRate();
        rate.SetDemand(PoseDemand.Adaptive);
        var processed = Simulate(rate, 0, 1.8, 15);

        Assert.Equal(28, processed.Count);
    }

    [Fact]
    public void PowerStateChangesTakeEffectOnTheNextFrame()
    {
        var rate = new AdaptiveFrameRate();
        rate.SetDemand(PoseDemand.Adaptive);
        ExpectProcessed(rate, 100);
        rate.Record(true, 100);
        ExpectProcessed(rate, 100 + (1.0 / 15));
        rate.Record(true, 100 + (1.0 / 15));

        rate.SetPowerConstrained(true);
        ExpectProcessed(rate, 100 + (2.0 / 15), false);
        ExpectProcessed(rate, 100 + (3.0 / 15));
        rate.Record(true, 100 + (3.0 / 15));

        rate.SetPowerConstrained(false);
        ExpectProcessed(rate, 100 + (4.0 / 15));
    }

    [Fact]
    public void AThrottledSourceStillProcessesAFrameAtLeastEveryIntervalPlusOneDeviceFrame()
    {
        foreach (var (constrained, interval) in new[] { (false, 0.2), (true, 0.5) })
        {
            var rate = new AdaptiveFrameRate();
            rate.SetDemand(PoseDemand.Adaptive);
            rate.SetPowerConstrained(constrained);
            var processed = Simulate(rate, 0, 60, 15).Where(t => t > 3).ToList();

            Assert.True(processed.Count > 20);
            foreach (var gap in Gaps(processed))
            {
                Assert.True(gap >= interval - (1.0 / 15) + 1e-6);
                Assert.True(gap <= interval + (1.0 / 15) + 1e-6);
            }

            Assert.Equal(constrained ? 60 : 150, processed.Count(t => t >= 10 && t < 40));
        }
    }

    // PoseDemand combination (AppModel+PoseDemand in the Mac app).

    [Fact]
    public void PlainTrackingAsksForTheAdaptiveRate()
    {
        Assert.Equal(PoseDemand.Adaptive, PoseDemandPolicy.Wanted(default));
    }

    [Fact]
    public void FocusLockOrMissingSwitchingAbilityAsksForTheReducedRate()
    {
        Assert.Equal(PoseDemand.Reduced, PoseDemandPolicy.Wanted(new PoseDemandInputs(FocusLocked: true)));
        Assert.Equal(PoseDemand.Reduced, PoseDemandPolicy.Wanted(new PoseDemandInputs(SwitchingUnavailable: true)));
    }

    [Fact]
    public void EveryLiveViewAsksForTheFullRate()
    {
        Assert.Equal(PoseDemand.Full, PoseDemandPolicy.Wanted(new PoseDemandInputs(GateTestRunning: true)));
        Assert.Equal(PoseDemand.Full, PoseDemandPolicy.Wanted(new PoseDemandInputs(SetupUsingCamera: true)));
        Assert.Equal(PoseDemand.Full, PoseDemandPolicy.Wanted(new PoseDemandInputs(PoseDebugVisible: true)));
        Assert.Equal(PoseDemand.Full, PoseDemandPolicy.Wanted(new PoseDemandInputs(FocusTestVisible: true)));
    }

    [Fact]
    public void TheFullRateBeatsTheReducedOne()
    {
        var inputs = new PoseDemandInputs(PoseDebugVisible: true, FocusLocked: true, SwitchingUnavailable: true);
        Assert.Equal(PoseDemand.Full, PoseDemandPolicy.Wanted(inputs));
    }
}
