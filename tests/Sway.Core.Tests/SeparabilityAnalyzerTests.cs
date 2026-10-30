namespace Sway.Core.Tests;

public class SeparabilityAnalyzerTests
{
    /// <summary>Two rounds of 60 samples per monitor, clusters drawn from a seeded RNG.</summary>
    private static List<PoseSample> Run(double[] yawCenters, double[] pitchCenters, double std, int seed = 7, double faceRate = 1.0)
    {
        var random = new Random(seed);
        var samples = new List<PoseSample>();
        double time = 0;
        for (var round = 1; round <= 2; round++)
        {
            for (var monitor = 0; monitor < yawCenters.Length; monitor++)
            {
                for (var i = 0; i < 60; i++)
                {
                    var face = random.NextDouble() < faceRate;
                    samples.Add(new PoseSample(time += 0.033, yawCenters[monitor] + (Gauss(random) * std), pitchCenters[monitor] + (Gauss(random) * std), 0, $"monitor {monitor + 1}", round, face));
                }
            }
        }

        return samples;
    }

    private static double Gauss(Random random) =>
        Math.Sqrt(-2 * Math.Log(1 - random.NextDouble())) * Math.Cos(2 * Math.PI * random.NextDouble());

    [Fact]
    public void FarApartTightClustersSeparateClearly()
    {
        var result = SeparabilityAnalyzer.Analyze(Run(new[] { -25.0, 20.0 }, new[] { 0.0, 3.0 }, 3));
        Assert.Equal(SeparabilityVerdict.SeparatesClearly, result.Verdict);
        Assert.Equal("separates clearly", result.VerdictText);
        Assert.True(result.HeldOutAccuracy >= 0.95);
        var pair = Assert.Single(result.Pairs);
        Assert.True(pair.DPrime >= 3);
        Assert.InRange(pair.CenterDistanceDegrees, 44, 47);
        Assert.InRange(pair.YawDifferenceDegrees, 44, 46);
    }

    [Fact]
    public void OverlappingClustersDoNotSeparate()
    {
        var result = SeparabilityAnalyzer.Analyze(Run(new[] { 0.0, 2.0 }, new[] { 0.0, 0.0 }, 6));
        Assert.Equal(SeparabilityVerdict.DoesNotSeparate, result.Verdict);
        Assert.Equal("does not separate", result.VerdictText);
    }

    [Fact]
    public void BorderlineClustersAreMarginal()
    {
        // Centers 2.2 std apart: about 86 percent of points land nearer their own center, d-prime about 2.2.
        var result = SeparabilityAnalyzer.Analyze(Run(new[] { 0.0, 11.0 }, new[] { 0.0, 0.0 }, 5));
        Assert.Equal(SeparabilityVerdict.Marginal, result.Verdict);
        Assert.InRange(result.HeldOutAccuracy!.Value, 0.85, 0.95);
    }

    [Fact]
    public void StatisticsAreReportedPerLabelInOrder()
    {
        var result = SeparabilityAnalyzer.Analyze(Run(new[] { -20.0, 10.0 }, new[] { 2.0, -4.0 }, 2));
        Assert.Equal(new[] { "monitor 1", "monitor 2" }, result.Labels.Select(l => l.Label));
        Assert.Equal(120, result.Labels[0].Count);
        Assert.Equal(-20, result.Labels[0].MeanYaw, 0);
        Assert.Equal(-4, result.Labels[1].MeanPitch, 0);
        Assert.Equal(2, result.Labels[0].StdYaw, 0);
    }

    [Fact]
    public void ThreeMonitorsProduceThreePairs()
    {
        var result = SeparabilityAnalyzer.Analyze(Run(new[] { -30.0, 0.0, 30.0 }, new[] { 0.0, 0.0, 0.0 }, 2));
        Assert.Equal(3, result.Pairs.Count);
        Assert.Equal(SeparabilityVerdict.SeparatesClearly, result.Verdict);
    }

    [Fact]
    public void HeldOutAccuracyIsTrainedOnOneRoundAndTestedOnTheOther()
    {
        // Round 1 sits at -20 and +20; round 2 drifts both by +15. Pooled statistics would hide the drift, but
        // training on one round and testing on the other still classifies correctly because the drift is small
        // compared with the gap. A large drift would break it.
        var samples = new List<PoseSample>();
        var random = new Random(3);
        for (var round = 1; round <= 2; round++)
        {
            var drift = round == 2 ? 15 : 0;
            foreach (var (label, center) in new[] { ("monitor 1", -20.0), ("monitor 2", 20.0) })
            {
                for (var i = 0; i < 50; i++)
                {
                    samples.Add(new PoseSample(i, center + drift + Gauss(random), Gauss(random), 0, label, round));
                }
            }
        }

        var good = SeparabilityAnalyzer.Analyze(samples);
        Assert.True(good.HeldOutAccuracy >= 0.95);

        var worse = samples.Select(s => s.Round == 2 ? s with { Yaw = s.Yaw + 20 } : s).ToList();
        Assert.True(SeparabilityAnalyzer.Analyze(worse).HeldOutAccuracy < 0.9);
    }

    [Fact]
    public void SingleRoundUsesATimeSplit()
    {
        var samples = Run(new[] { -25.0, 20.0 }, new[] { 0.0, 0.0 }, 3).Where(s => s.Round == 1).ToList();
        var result = SeparabilityAnalyzer.Analyze(samples);
        Assert.Equal(SeparabilityVerdict.SeparatesClearly, result.Verdict);
    }

    [Fact]
    public void FaceRateCountsFramesWithoutAFace()
    {
        var result = SeparabilityAnalyzer.Analyze(Run(new[] { -25.0, 20.0 }, new[] { 0.0, 0.0 }, 3, faceRate: 0.8));
        Assert.InRange(result.FaceRate, 0.7, 0.9);
    }

    [Fact]
    public void TooFewSamplesIsInsufficient()
    {
        var result = SeparabilityAnalyzer.Analyze(new[]
        {
            new PoseSample(0, 1, 1, 0, "monitor 1", 1),
            new PoseSample(1, 9, 1, 0, "monitor 2", 1),
        });
        Assert.Equal(SeparabilityVerdict.InsufficientData, result.Verdict);
        Assert.Null(result.HeldOutAccuracy);
    }

    [Fact]
    public void EmptyAndUnlabeledInputIsInsufficient()
    {
        Assert.Equal(SeparabilityVerdict.InsufficientData, SeparabilityAnalyzer.Analyze(Array.Empty<PoseSample>()).Verdict);
        var unlabeled = new[] { new PoseSample(0, 1, 1, 0, string.Empty) };
        Assert.Empty(SeparabilityAnalyzer.Analyze(unlabeled).Labels);
    }

    [Fact]
    public void IdenticalCentersGiveZeroDPrime()
    {
        var result = SeparabilityAnalyzer.Analyze(Run(new[] { 5.0, 5.0 }, new[] { 5.0, 5.0 }, 0.0001));
        Assert.True(result.Pairs[0].DPrime < 0.1);
    }
}
