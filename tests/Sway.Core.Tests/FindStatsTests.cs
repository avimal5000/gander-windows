using Sway.Core.Pose;

namespace Sway.Core.Tests;

public class FindStatsTests
{
    [Fact]
    public void CountsEachSourceAndTheLongestLostRun()
    {
        var stats = new FindStats();
        FaceSource[] sequence =
        [
            FaceSource.Detector, FaceSource.Tracked, FaceSource.NotFound, FaceSource.NotFound, FaceSource.Fallback,
            FaceSource.NotFound, FaceSource.NotFound, FaceSource.NotFound, FaceSource.Tracked,
        ];
        foreach (var source in sequence)
        {
            stats.Add(source);
        }

        Assert.Equal(9, stats.Frames);
        Assert.Equal(2, stats.Tracked);
        Assert.Equal(1, stats.Detector);
        Assert.Equal(1, stats.Fallback);
        Assert.Equal(5, stats.NotFound);
        Assert.Equal(3, stats.LongestLostRun);
        Assert.Contains("longest lost run 3", stats.Summary());
    }

    [Fact]
    public void EmptyStatsAreZero()
    {
        var stats = new FindStats();
        Assert.Equal(0, stats.Frames);
        Assert.Equal(0, stats.LongestLostRun);
    }
}
