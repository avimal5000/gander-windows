using Gander.Core.Controller;
using Gander.Core.Platform;
using Gander.Core.Runtime;

namespace Gander.Core.Tests.Controller;

public class SwitchDiagnosticsTests
{
    private static readonly DisplayId Right = Fixtures.RightId;
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);

    [Fact]
    public void SamplesAreCountedAndTheLastFacePoseIsKept()
    {
        var d = new SwitchDiagnostics();
        d.RecordSample(new HeadPose(5, -2, 1), Now);
        d.RecordSample(null, Now.AddSeconds(2));

        Assert.Equal(2, d.SamplesReceived);
        Assert.Equal(1, d.SamplesWithFace);
        Assert.Equal(5, d.LastPose?.Yaw);
        Assert.Equal(Now, d.LastPoseAt);
        Assert.Equal(Now.AddSeconds(2), d.LastSampleAt);
        Assert.False(d.LastSampleHadFace);
    }

    [Fact]
    public void ResultsAndSkipsAreCountedAndResetKeepsThePose()
    {
        var d = new SwitchDiagnostics();
        d.RecordSample(new HeadPose(1, 2, 3), Now);
        d.RecordRequest();
        d.RecordSkippedByHold();
        d.RecordSkippedInFlight();
        d.RecordResult(new FocusResult.NoWindow(Right));
        d.RecordResult(new FocusResult.Failed(null, "x"));
        d.RecordResult(new FocusResult.Focused(Fixtures.Window(7, "Edge", Fixtures.Right), FocusMethod.Plain, 1));

        Assert.Equal((1, 1, 1, 1, 1, 1), (d.SwitchRequests, d.SkippedByHold, d.SkippedInFlight, d.NoWindow, d.Failed, d.Focused));
        d.ResetCounts();
        Assert.Equal(0, d.SamplesReceived + d.SwitchRequests + d.Focused + d.NoWindow + d.Failed + d.SkippedByHold + d.SkippedInFlight);
        Assert.NotNull(d.LastPose);
    }

    [Fact]
    public async Task TheControllerCountsSamplesRequestsAndResults()
    {
        using var h = Harness.Make(layout: Fixtures.TwoDisplays, calibrated: true);
        await h.StartTracking();
        h.Drain();
        h.Focus.Result = TrackingTests.FocusedRight();
        TrackingTests.ScriptLook(h, Right);

        h.FeedBurst(1.0, 1.2, 1.4);

        var d = h.Controller.Diagnostics;
        Assert.Equal(3, d.SamplesReceived);
        Assert.Equal(3, d.SamplesWithFace);
        Assert.Equal(3, d.SamplesProcessed);
        Assert.Equal(1, d.SwitchRequests);
        Assert.Equal(1, d.Focused);
        Assert.Equal(0, d.SkippedByHold);
    }

    [Fact]
    public async Task ARequestDuringAHoldIsCountedAsSkipped()
    {
        using var h = Harness.Make(layout: Fixtures.TwoDisplays, calibrated: true);
        await h.StartTracking();
        h.Drain();
        h.Controller.SetGanderInFront(true);
        TrackingTests.ScriptLook(h, Right, ignoresLock: true);

        h.FeedBurst(1.0, 1.4, 1.6);

        var d = h.Controller.Diagnostics;
        Assert.Equal(2, d.SwitchRequests);
        Assert.Equal(2, d.SkippedByHold);
        Assert.Empty(h.Focus.FocusCalls);
        Assert.True(h.Controller.CurrentHoldInputs.GanderInFront);
    }

    [Fact]
    public async Task AStillRunningAttemptIsCountedAsSkippedInFlight()
    {
        using var h = Harness.Make(layout: Fixtures.TwoDisplays, calibrated: true);
        await h.StartTracking();
        h.Drain();
        h.Focus.HoldFocusRequests();
        TrackingTests.ScriptLook(h, Right);

        h.FeedBurst(1.0, 1.2, 1.4, 1.5);

        Assert.Equal(1, h.Controller.Diagnostics.SkippedInFlight);
        h.Focus.Release();
        h.Drain();
    }

    [Fact]
    public async Task OneLineIsLoggedEveryFiveSecondsWhileTracking()
    {
        using var h = Harness.Make(layout: Fixtures.TwoDisplays, calibrated: true);
        await h.StartTracking();
        h.Drain();
        TrackingTests.ScriptLook(h, Right);

        h.FeedBurst(1.0, 1.2);
        h.Advance(1);
        h.Feed(1.3);
        h.Advance(5);
        h.Feed(1.4);

        var lines = h.Controller.LogLines.Where(l => l.StartsWith("Switch diagnostics:", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, lines.Count);
        Assert.Contains("samples 1, face 1, processed 1", lines[0], StringComparison.Ordinal);
        Assert.Contains("hold none [lock False, gander in front False", lines[0], StringComparison.Ordinal);
        h.Controller.SetGanderInFront(false);
    }
}
