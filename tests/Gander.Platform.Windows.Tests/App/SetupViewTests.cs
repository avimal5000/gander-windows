using Gander.App.Presentation;
using Gander.Core;
using Gander.Core.Calibration;

namespace Gander.Platform.Windows.Tests.App;

public sealed class SetupViewTests
{
    private static SetupViewInput Input(
        SetupStep step,
        bool canStart = true,
        CalibrationMessage? message = null,
        GateProgress? progress = null,
        SetupResult? result = null,
        string? prompt = null,
        int introPage = 0) =>
        new(step, "Looks good.", "Open settings text", canStart, message, progress is not null, prompt, progress, result, introPage);

    private static GateProgress Progress(GatePhase phase, double elapsed, double duration) =>
        new(phase, 2, 4, 1, 2, 2, 2, new DisplayInfo("b", 2, "Right", new DisplayRect(0, 0, 1920, 1080), false, false), elapsed, duration, 0, 0);

    [Fact]
    public void WelcomeStartsOnTheFirstIntroPageWithNextAndNoBack()
    {
        var view = SetupViewBuilder.Build(Input(SetupStep.Welcome));
        Assert.Equal("Welcome to Gander", view.Title);
        Assert.Equal("Look at a screen. Start typing.", view.Lines[0]);
        Assert.Equal(new SetupButton("Next", SetupAction.IntroNext), view.Primary);
        Assert.Null(view.Secondary);
        Assert.Equal(new SetupButton("Skip intro", SetupAction.IntroSkip), view.Intro!.Skip);
        Assert.Equal(IntroArt.Scene, view.Intro.Content.Art);
        Assert.Equal("Fig. 1", view.Intro.Content.Figure);
        Assert.Equal("Gander at work: the head turns, focus follows, the pointer stays.", view.Intro.Content.Caption);
        Assert.Null(view.StepLine);
    }

    [Fact]
    public void EveryIntroPageHasItsWordsAndTheMiddleOnesHaveBack()
    {
        var moves = SetupViewBuilder.Build(Input(SetupStep.Welcome, introPage: 1));
        Assert.Equal("Look. Settle. Type.", moves.Title);
        Assert.Equal(SetupPresentation.Moves, moves.Lines);
        Assert.Equal(SetupAction.IntroBack, moves.Secondary!.Action);
        Assert.Equal(SetupAction.IntroNext, moves.Primary!.Action);

        var privacy = SetupViewBuilder.Build(Input(SetupStep.Welcome, introPage: 2));
        Assert.Equal("Your camera stays yours.", privacy.Title);
        Assert.Equal(3, privacy.Lines.Count);
        Assert.Equal(IntroArt.Privacy, privacy.Intro!.Content.Art);
        Assert.NotNull(privacy.Intro.Skip);
    }

    [Fact]
    public void TheLastIntroPageSetsGanderUpAndOffersNoSkip()
    {
        var view = SetupViewBuilder.Build(Input(SetupStep.Welcome, introPage: 3));
        Assert.Equal("Find Gander in the system tray", view.Title);
        Assert.Equal(new SetupButton("Set Up Gander", SetupAction.GetStarted), view.Primary);
        Assert.Equal(SetupAction.IntroBack, view.Secondary!.Action);
        Assert.Null(view.Intro!.Skip);
        Assert.Contains(SetupPresentation.TrayText, view.Lines);
        Assert.Equal("Page 4 of 4", view.Intro.PageLine);
    }

    [Fact]
    public void AnOutOfRangeIntroPageIsClamped()
    {
        Assert.Equal(0, SetupViewBuilder.Build(Input(SetupStep.Welcome, introPage: -3)).Intro!.Page);
        Assert.Equal(3, SetupViewBuilder.Build(Input(SetupStep.Welcome, introPage: 9)).Intro!.Page);
    }

    [Fact]
    public void IntroCopyIsComplete()
    {
        Assert.Equal(SetupFlow.IntroPageCount, SetupPresentation.IntroPages.Count);
        Assert.All(SetupPresentation.IntroPages, page => Assert.False(string.IsNullOrWhiteSpace(page.Caption)));
        Assert.Equal(3, SetupPresentation.Moves.Count);
        Assert.Equal(["Allow the camera when Windows asks.", "Look at each screen once."], SetupPresentation.NextItems);
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
        Assert.Equal(SetupAction.Finish, one.Primary!.Action);
        Assert.Equal("Done", one.Primary.Label);
        Assert.Equal(SetupPresentation.CalibrateLater, one.Message);
        Assert.False(one.MessageIsError);
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
