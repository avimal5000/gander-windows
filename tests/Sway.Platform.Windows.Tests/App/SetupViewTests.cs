using Sway.App.Presentation;
using Sway.Core;
using Sway.Core.Calibration;

namespace Sway.Platform.Windows.Tests.App;

public sealed class SetupViewTests
{
    private static SetupViewInput Input(
        SetupStep step,
        bool canStart = true,
        CalibrationMessage? message = null,
        GateProgress? progress = null,
        SetupResult? result = null,
        string? prompt = null) =>
        new(step, "Looks good.", "Open settings text", canStart, message, progress is not null, prompt, progress, result);

    private static GateProgress Progress(GatePhase phase, double elapsed, double duration) =>
        new(phase, 2, 4, 1, 2, 2, 2, new DisplayInfo("b", 2, "Right", new DisplayRect(0, 0, 1920, 1080), false, false), elapsed, duration, 0, 0);

    [Fact]
    public void WelcomeShowsPrivacyAndGetStarted()
    {
        var view = SetupViewBuilder.Build(Input(SetupStep.Welcome));
        Assert.Contains(SetupPresentation.WelcomePrivacy, view.Lines);
        Assert.Equal(new SetupButton("Get Started", SetupAction.GetStarted), view.Primary);
        Assert.Null(view.StepLine);
    }

    [Fact]
    public void NeedsYouOpensCameraSettings()
    {
        var view = SetupViewBuilder.Build(Input(SetupStep.NeedsYou));
        Assert.Equal(SetupAction.OpenCameraSettings, view.Primary!.Action);
        Assert.Equal("Step 1 of 4", view.StepLine);
        Assert.Contains("Open settings text", view.Lines);
    }

    [Fact]
    public void CameraCheckShowsFramingAndNeedsTwoMonitors()
    {
        var ok = SetupViewBuilder.Build(Input(SetupStep.CameraCheck));
        Assert.Equal("Looks good.", ok.Highlight);
        Assert.True(ok.Primary!.Enabled);

        var one = SetupViewBuilder.Build(Input(SetupStep.CameraCheck, canStart: false));
        Assert.False(one.Primary!.Enabled);
        Assert.Equal(SetupPresentation.NeedsSecondDisplay, one.Message);
        Assert.True(one.MessageIsError);
    }

    [Fact]
    public void CalibratingShowsPromptProgressAndCancel()
    {
        var view = SetupViewBuilder.Build(Input(SetupStep.Calibrating, progress: Progress(GatePhase.Hold, 1, 2), prompt: "Look at Right"));
        Assert.Equal("Look at Right", view.Highlight);
        Assert.Equal(SetupAction.CancelCalibration, view.Primary!.Action);
        Assert.Equal(0.375, view.Progress!.Fraction, 6);
        Assert.Contains("Look 2 of 4", view.Progress.Caption, System.StringComparison.Ordinal);
        Assert.Contains("1.0 s left", view.Progress.Caption, System.StringComparison.Ordinal);
    }

    [Fact]
    public void CalibratingBeforeTheFirstLookSaysStarting()
    {
        var view = SetupViewBuilder.Build(Input(SetupStep.Calibrating));
        Assert.Null(view.Progress);
        Assert.Contains("camera", view.Highlight, System.StringComparison.Ordinal);
    }

    [Fact]
    public void DoneOffersTrackingWhenSavedAndRetryWhenNot()
    {
        var saved = SetupViewBuilder.Build(Input(SetupStep.Done, result: new SetupResult(CalibrationQuality.Good, "Calibration looks good.")));
        Assert.Equal(SetupAction.StartTracking, saved.Primary!.Action);
        Assert.Equal(SetupAction.CalibrateAgain, saved.Secondary!.Action);
        Assert.Equal("Quality: good", saved.Highlight);

        var failed = SetupViewBuilder.Build(Input(SetupStep.Done, result: SetupResult.Failed("No.")));
        Assert.Equal(SetupAction.CalibrateAgain, failed.Primary!.Action);
        Assert.Null(failed.Secondary);
    }

    [Theory]
    [InlineData(GatePhase.Settle, 0.0, 1.5, 0.0)]
    [InlineData(GatePhase.Settle, 0.2, 1.5, 0.5)]
    [InlineData(GatePhase.Settle, 1.0, 1.5, 1.0)]
    [InlineData(GatePhase.Hold, 1.0, 2.5, 1.0)]
    [InlineData(GatePhase.Hold, 2.3, 2.5, 0.5)]
    [InlineData(GatePhase.Hold, 2.5, 2.5, 0.0)]
    public void OverlayFades(GatePhase phase, double elapsed, double duration, double expected) =>
        Assert.Equal(expected, PromptOverlayMath.Opacity(phase, elapsed, duration), 6);

    [Fact]
    public void MarkerSizeScalesWithTheMonitor()
    {
        Assert.Equal(0.22 * 1080, PromptOverlayMath.MarkerDiameter(1920, 1080), 6);
        Assert.Equal(96, PromptOverlayMath.MarkerDiameter(200, 200));
        Assert.Equal(1.5, PromptOverlayMath.ScaleFromDpi(150));
        Assert.Equal(1.0, PromptOverlayMath.ScaleFromDpi(null));
    }
}
