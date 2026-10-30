using Sway.Core.Runtime;

namespace Sway.Core.Tests.Runtime;

public class RetryBackoffTests
{
    [Fact]
    public void OpenFailuresBackOffFromOneToTenSeconds()
    {
        var backoff = new RetryBackoff();
        var delays = Enumerable.Range(0, 7).Select(_ => backoff.NextOpenFailureDelay()).ToArray();
        Assert.Equal(new double[] { 1, 2, 5, 10, 10, 10, 10 }, delays);
    }

    [Fact]
    public void StallRebuildsBackOffFromTenToSixtySeconds()
    {
        var backoff = new RetryBackoff();
        var delays = Enumerable.Range(0, 6).Select(_ => backoff.NextStallRebuildDelay()).ToArray();
        Assert.Equal(new double[] { 10, 30, 60, 60, 60, 60 }, delays);
    }

    [Fact]
    public void TheTwoSequencesAdvanceIndependently()
    {
        var backoff = new RetryBackoff();
        Assert.Equal(1, backoff.NextOpenFailureDelay());
        Assert.Equal(2, backoff.NextOpenFailureDelay());
        Assert.Equal(10, backoff.NextStallRebuildDelay());
        Assert.Equal(5, backoff.NextOpenFailureDelay());
        Assert.Equal(30, backoff.NextStallRebuildDelay());
    }

    [Fact]
    public void FramesResetBothSequences()
    {
        var backoff = new RetryBackoff();
        for (var i = 0; i < 5; i++)
        {
            backoff.NextOpenFailureDelay();
            backoff.NextStallRebuildDelay();
        }

        backoff.FramesFlowed();
        Assert.Equal(new RetryBackoff(), backoff);
        Assert.Equal(1, backoff.NextOpenFailureDelay());
        Assert.Equal(10, backoff.NextStallRebuildDelay());
    }

    [Fact]
    public void ALongRunNeverGrowsPastTheLastDelayOrOverflows()
    {
        var backoff = new RetryBackoff();
        double last = 0;
        for (var i = 0; i < 10_000; i++)
        {
            backoff.NextStallRebuildDelay();
            last = backoff.NextOpenFailureDelay();
        }

        Assert.Equal(10, last);
        Assert.Equal(60, backoff.NextStallRebuildDelay());
    }

    [Fact]
    public void TheFirstStallDelayIsTenSeconds()
    {
        // The Mac test compares with FrameWatchdog.defaultRetryInterval, which is not ported yet (camera module).
        Assert.Equal(10, RetryBackoff.StallRebuildDelays[0]);
    }
}
