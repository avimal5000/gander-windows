using System;
using System.Collections.Generic;
using System.Globalization;
using Gander.Core.Calibration;

namespace Gander.App.Presentation;

/// <summary>What a button in the Set up window does.</summary>
public enum SetupAction
{
    GetStarted,
    IntroNext,
    IntroBack,
    IntroSkip,
    OpenCameraSettings,
    StartCalibration,
    CancelCalibration,
    CalibrateAgain,
    StartTracking,
    Finish,
}

/// <summary>A button: its words, what it does, and whether it can be pressed.</summary>
public sealed record SetupButton(string Label, SetupAction Action, bool Enabled = true);

/// <summary>The progress bar and its caption.</summary>
public sealed record SetupProgress(double Fraction, string Caption);

/// <summary>The intro on the welcome step: which page, its content, and the quiet "Skip intro" button (null on the last page).</summary>
public sealed record SetupIntro(int Page, int PageCount, IntroPage Content, string PageLine, SetupButton? Skip);

/// <summary>Everything the window shows for one moment (pure data; the WPF window only copies it onto controls).</summary>
public sealed record SetupView(
    string Title,
    string? StepLine,
    IReadOnlyList<string> Lines,
    string? Highlight,
    string? Message,
    bool MessageIsError,
    SetupProgress? Progress,
    SetupButton? Primary,
    SetupButton? Secondary,
    SetupIntro? Intro = null);

/// <summary>The session's state, copied so the view can be built without a session.</summary>
public sealed record SetupViewInput(
    SetupStep Step,
    string FramingText,
    string NeedsYouText,
    bool CanStartCalibration,
    CalibrationMessage? Message,
    bool CalibrationActive,
    string? Prompt,
    GateProgress? Progress,
    SetupResult? Result,
    int IntroPage = 0);

/// <summary>Builds the window's content from the session's state, step by step.</summary>
public static class SetupViewBuilder
{
    /// <summary>The view for <paramref name="input"/>.</summary>
    public static SetupView Build(SetupViewInput input)
    {
        var title = SetupPresentation.Title(input.Step);
        var stepLine = SetupPresentation.StepLine(input.Step);
        var message = input.Message?.Text;
        var isError = input.Message?.IsError ?? false;

        switch (input.Step)
        {
            case SetupStep.Welcome:
                return BuildIntro(input.IntroPage);

            case SetupStep.NeedsYou:
                return new SetupView(
                    title, stepLine, [input.NeedsYouText], null, null, false, null,
                    new SetupButton("Open Windows Settings", SetupAction.OpenCameraSettings), null);

            case SetupStep.CameraCheck:
                if (!input.CanStartCalibration)
                {
                    // One monitor: the setup ends here (Mac SPEC D50); the menu asks once a second monitor is in.
                    return new SetupView(
                        title, stepLine, [SetupPresentation.CameraAdvice], input.FramingText, message ?? SetupPresentation.CalibrateLater,
                        message is not null && isError, null, new SetupButton("Done", SetupAction.Finish), null);
                }

                return new SetupView(
                    title, stepLine, [SetupPresentation.CameraAdvice], input.FramingText, message, isError, null,
                    new SetupButton("Start calibration", SetupAction.StartCalibration), null);

            case SetupStep.Calibrating:
                return new SetupView(
                    title, stepLine, [], input.Prompt ?? "Starting the camera…", message, isError, ProgressFor(input.Progress),
                    new SetupButton("Cancel", SetupAction.CancelCalibration), null);

            default:
                var result = input.Result;
                var lines = new List<string>();
                if (result is not null)
                {
                    lines.Add(result.Text);
                }

                var primary = result is { IsSaved: true }
                    ? new SetupButton("Start tracking", SetupAction.StartTracking)
                    : new SetupButton("Calibrate again", SetupAction.CalibrateAgain);
                var secondary = result is { IsSaved: true } ? new SetupButton("Calibrate again", SetupAction.CalibrateAgain) : null;
                return new SetupView(title, stepLine, lines, result is null ? null : QualityLabel(result.Quality), message, isError, null, primary, secondary);
        }
    }

    /// <summary>
    /// The welcome step: one intro page. Back shows from the second page on; the primary button turns the page, and on
    /// the last page sets Gander up. "Skip intro" shows on every page but the last. An out-of-range page is clamped.
    /// </summary>
    public static SetupView BuildIntro(int page)
    {
        var last = SetupFlow.IntroPageCount - 1;
        page = Math.Clamp(page, 0, last);
        var content = SetupPresentation.IntroPages[page];
        var primary = page == last
            ? new SetupButton(SetupPresentation.WelcomeButton, SetupAction.GetStarted)
            : new SetupButton(SetupPresentation.IntroNextButton, SetupAction.IntroNext);
        var back = page > 0 ? new SetupButton(SetupPresentation.IntroBackButton, SetupAction.IntroBack) : null;
        var skip = page < last ? new SetupButton(SetupPresentation.IntroSkipButton, SetupAction.IntroSkip) : null;
        var lines = new List<string>();
        if (content.Headline is not null)
        {
            lines.Add(content.Headline);
        }

        lines.AddRange(content.Lines);
        return new SetupView(
            content.Title, null, lines, null, null, false, null, primary, back,
            new SetupIntro(page, SetupFlow.IntroPageCount, content, SetupPresentation.IntroPageLine(page), skip));
    }

    /// <summary>"Step 2 of 4 · 1.5 s left" style caption.</summary>
    public static SetupProgress? ProgressFor(GateProgress? progress)
    {
        if (progress is null)
        {
            return null;
        }

        var seconds = progress.Remaining.ToString("0.0", CultureInfo.InvariantCulture);
        var phase = progress.Phase == GatePhase.Settle ? "Get ready" : "Hold still";
        var overall = (progress.StepNumber - 1 + progress.Fraction) / Math.Max(1, progress.StepCount);
        return new SetupProgress(
            Math.Clamp(overall, 0, 1),
            $"Look {progress.StepNumber} of {progress.StepCount} · {phase} · {seconds} s left");
    }

    /// <summary>A short word for the quality.</summary>
    public static string QualityLabel(CalibrationQuality quality) => quality switch
    {
        CalibrationQuality.Good => "Quality: good",
        CalibrationQuality.Fair => "Quality: fair",
        CalibrationQuality.Poor => "Quality: poor",
        _ => "Nothing was saved",
    };
}

/// <summary>Sizes and fades for the "Look here" marker (pure).</summary>
public static class PromptOverlayMath
{
    /// <summary>Seconds the marker takes to fade in or out.</summary>
    public const double FadeSeconds = 0.4;

    /// <summary>The marker's opacity: fades in during the first part of the settle, out during the end of the hold.</summary>
    public static double Opacity(GatePhase phase, double elapsed, double duration)
    {
        if (phase == GatePhase.Settle)
        {
            return Math.Clamp(elapsed / FadeSeconds, 0, 1);
        }

        return Math.Clamp((duration - elapsed) / FadeSeconds, 0, 1);
    }

    /// <summary>Ring diameter in pixels for a monitor of the given size: 22% of the shorter side, never under 96.</summary>
    public static double MarkerDiameter(double widthPixels, double heightPixels) =>
        Math.Max(96, Math.Min(widthPixels, heightPixels) * 0.22);

    /// <summary>Scale factor from a DPI value (96 is 1.0); 1.0 when the value is unusable.</summary>
    public static double ScaleFromDpi(int? percent) => percent is > 0 ? percent.Value / 100.0 : 1.0;
}
