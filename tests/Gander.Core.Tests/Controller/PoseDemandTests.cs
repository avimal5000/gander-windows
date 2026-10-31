using Gander.Core.Platform;
using Gander.Core.Runtime;

namespace Gander.Core.Tests.Controller;

/// <summary>Port of the Mac AppModelPoseDemandTests (Mac CONTRACT 6.5.5 item 2, SPEC D45), minus the Accessibility cases.</summary>
public class PoseDemandTests
{
    private static async Task<Harness> TrackingHarness()
    {
        var h = Harness.Make(calibrated: true);
        await h.StartTracking();
        h.Drain();
        return h;
    }

    // MARK: Tracking

    [Fact]
    public async Task PlainTrackingAsksForTheAdaptiveRateOnceAfterTheStart()
    {
        using var h = await TrackingHarness();

        Assert.Equal([new FakePoseSource.DemandCall(PoseDemand.Adaptive, 1)], h.Source.DemandCalls);
    }

    [Fact]
    public void NothingIsSentBeforeTheCameraRuns()
    {
        using var h = Harness.Make(calibrated: true);

        h.Controller.SetFocusLock(true);
        h.Controller.SetFocusLock(false);

        Assert.Empty(h.Source.DemandCalls); // A start resets the source anyway, and says what is needed after it.
    }

    [Fact]
    public async Task EveryStartSaysWhatIsNeededAgainEvenWhenItIsTheSame()
    {
        using var h = await TrackingHarness();

        // Choosing the built-in camera explicitly changes the configuration, which restarts the source.
        await h.Controller.SelectCameraAsync(Fixtures.BuiltInCamera.Id);
        h.Drain();

        Assert.Equal(2, h.Source.StartCount);
        Assert.Equal(
            [new FakePoseSource.DemandCall(PoseDemand.Adaptive, 1), new FakePoseSource.DemandCall(PoseDemand.Adaptive, 2)],
            h.Source.DemandCalls);
    }

    [Fact]
    public async Task SamplesNeverCauseDemandCalls()
    {
        using var h = await TrackingHarness();
        var calls = h.Source.DemandCalls.Count;

        for (var i = 1; i <= 60; i++)
        {
            h.Feed(i / 15.0);
        }

        for (var i = 1; i <= 30; i++)
        {
            h.Feed(4 + (i / 15.0), face: false);
        }

        Assert.Equal(calls, h.Source.DemandCalls.Count);
    }

    // MARK: Reduced

    [Fact]
    public async Task FocusLockAsksForTheReducedRateAndUnlockingRestoresTheAdaptiveOne()
    {
        using var h = await TrackingHarness();

        h.Controller.SetFocusLock(true);
        Assert.Equal([PoseDemand.Adaptive, PoseDemand.Reduced], h.Source.Demands);
        h.Controller.SetFocusLock(true);
        Assert.Equal([PoseDemand.Adaptive, PoseDemand.Reduced], h.Source.Demands); // The same demand is not sent again.
        h.Controller.SetFocusLock(false);
        Assert.Equal([PoseDemand.Adaptive, PoseDemand.Reduced, PoseDemand.Adaptive], h.Source.Demands);
    }

    // MARK: Full

    [Fact]
    public async Task ACalibrationRunAsksForTheFullRateAndTheEndGivesTheAdaptiveRateBack()
    {
        using var h = await TrackingHarness();

        await h.Controller.SetCalibrationRunningAsync(true);
        h.Drain();
        Assert.Equal(PoseDemand.Full, h.Source.Demands.Last());
        Assert.Equal(1, h.Source.StartCount); // The running source is kept: same configuration.

        await h.Controller.SetCalibrationRunningAsync(false);
        h.Drain();
        Assert.Equal(PoseDemand.Adaptive, h.Source.Demands.Last());
    }

    [Fact]
    public async Task ACalibrationAloneStartsTheCameraAtTheFullRate()
    {
        using var h = Harness.Make(calibrated: true);

        await h.Controller.SetCalibrationRunningAsync(true);
        h.Drain();

        Assert.Equal([new FakePoseSource.DemandCall(PoseDemand.Full, 1)], h.Source.DemandCalls);
    }

    [Fact]
    public async Task ThePoseDebugWindowAsksForTheFullRateWhileItShowsLiveContent()
    {
        using var h = await TrackingHarness();

        h.Controller.SetPoseDebugVisible(true);
        Assert.Equal(PoseDemand.Full, h.Source.Demands.Last());
        h.Controller.SetPoseDebugVisible(false); // Minimized, covered closed, or closing.
        Assert.Equal(PoseDemand.Adaptive, h.Source.Demands.Last());
        h.Controller.SetPoseDebugVisible(true);
        Assert.Equal(PoseDemand.Full, h.Source.Demands.Last());
    }

    [Fact]
    public async Task TheFocusTestWindowAsksForTheFullRate()
    {
        using var h = await TrackingHarness();

        h.Controller.SetFocusTestVisible(true);
        Assert.Equal(PoseDemand.Full, h.Source.Demands.Last());
        h.Controller.SetFocusTestVisible(false);
        Assert.Equal(PoseDemand.Adaptive, h.Source.Demands.Last());
    }

    [Fact]
    public async Task TheFullRateBeatsTheReducedOne()
    {
        using var h = await TrackingHarness();

        h.Controller.SetFocusLock(true);
        Assert.Equal(PoseDemand.Reduced, h.Source.Demands.Last());
        h.Controller.SetPoseDebugVisible(true);
        Assert.Equal(PoseDemand.Full, h.Source.Demands.Last()); // Pose Debug needs live numbers even with Focus Lock on.
        h.Controller.SetPoseDebugVisible(false);
        Assert.Equal(PoseDemand.Reduced, h.Source.Demands.Last());
    }

    [Fact]
    public async Task StoppingTheCameraForgetsTheDemandSoTheNextStartSendsIt()
    {
        using var h = await TrackingHarness();

        await h.SetTracking(false);
        h.Controller.SetFocusLock(true);
        Assert.Equal([PoseDemand.Adaptive], h.Source.Demands); // No stream, nothing sent.

        await h.SetTracking(true);
        Assert.Equal(new FakePoseSource.DemandCall(PoseDemand.Reduced, 2), h.Source.DemandCalls.Last());
    }
}
