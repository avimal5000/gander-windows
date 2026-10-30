namespace Sway.Core.Tests;

/// <summary>Port of the Mac SplitMix64Tests (the test-target random generator behind the synthetic scenarios).</summary>
public class SplitMix64Tests
{
    [Fact]
    public void SameSeedGivesTheSameStream()
    {
        var first = new SplitMix64(42);
        var second = new SplitMix64(42);
        var a = Enumerable.Range(0, 20).Select(_ => first.NextGaussian()).ToList();
        var b = Enumerable.Range(0, 20).Select(_ => second.NextGaussian()).ToList();
        Assert.Equal(a, b);
    }

    [Fact]
    public void DifferentSeedsDiffer() => Assert.NotEqual(new SplitMix64(2).Next(), new SplitMix64(1).Next());

    [Fact]
    public void GaussianHasRoughlyUnitMomentsAndUnitIntervalIsBounded()
    {
        var rng = new SplitMix64(7);
        var values = Enumerable.Range(0, 20_000).Select(_ => rng.NextGaussian()).ToList();
        var mean = values.Sum() / values.Count;
        var variance = values.Sum(v => (v - mean) * (v - mean)) / (values.Count - 1);
        Assert.True(Math.Abs(mean) < 0.05);
        Assert.True(Math.Abs(variance - 1) < 0.05);
        var units = Enumerable.Range(0, 1000).Select(_ => rng.NextUnit()).ToList();
        Assert.All(units, u => Assert.True(u >= 0 && u < 1));
    }

    /// <summary>Not in the Swift suite: pins the generator to the reference SplitMix64 sequence for seed 1234567.</summary>
    [Fact]
    public void MatchesTheReferenceSplitMix64Sequence()
    {
        var rng = new SplitMix64(1234567);
        Assert.Equal(6457827717110365317UL, rng.Next());
        Assert.Equal(3203168211198807973UL, rng.Next());
        Assert.Equal(9817491932198370423UL, rng.Next());
    }
}
