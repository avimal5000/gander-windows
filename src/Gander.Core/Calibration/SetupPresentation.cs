using System.Collections.Generic;

namespace Gander.Core.Calibration;

/// <summary>The picture that goes with an intro page.</summary>
public enum IntroArt
{
    /// <summary>Two monitors, a head that turns, a focus ring that follows.</summary>
    Scene,

    /// <summary>Three drawn moves: look, settle, type.</summary>
    Moves,

    /// <summary>The goose in a padlock.</summary>
    Privacy,

    /// <summary>The taskbar tray strip with the goose.</summary>
    Tray,
}

/// <summary>One page of the intro: its picture, words and figure label.</summary>
/// <param name="Art">Which picture the page draws.</param>
/// <param name="Title">The page title.</param>
/// <param name="Headline">A line under the title, or null.</param>
/// <param name="Figure">"Fig. 1".</param>
/// <param name="Caption">The italic caption under the plate.</param>
/// <param name="Lines">The page's text lines (the three moves, the promises, the paragraph).</param>
public sealed record IntroPage(IntroArt Art, string Title, string? Headline, string Figure, string Caption, IReadOnlyList<string> Lines);

/// <summary>Wording for the Set Up Gander window that more than one place needs (SPEC F20, F26), in Windows words.</summary>
public static class SetupPresentation
{
    /// <summary>Title of the welcome step (its first intro page).</summary>
    public const string WelcomeTitle = "Welcome to Gander";

    /// <summary>The headline under the welcome title.</summary>
    public const string WelcomeHeadline = "Look at a screen. Start typing.";

    /// <summary>Caption of the first plate.</summary>
    public const string SceneCaption = "Gander at work: the head turns, focus follows, the pointer stays.";

    /// <summary>The words typed into the second monitor in the first plate's scene.</summary>
    public const string SceneTyped = "ship it";

    /// <summary>The three moves on the second intro page, in order.</summary>
    public static readonly IReadOnlyList<string> Moves =
    [
        "Gander watches your head turn",
        "You settle on another screen",
        "Focus follows your gaze; the pointer stays where it was",
    ];

    /// <summary>The three promises on the privacy page.</summary>
    public static readonly IReadOnlyList<string> PrivacyLines =
    [
        "Gander reads only which way your head is turned, on this PC.",
        "No video or images are saved or sent.",
        "The camera light goes off when you pause.",
    ];

    /// <summary>The paragraph under the tray strip on the last intro page.</summary>
    public const string TrayText =
        "Click the goose to pause, lock or open Settings. If you do not see it, click the arrow beside the clock. "
        + "Gander has no window until you open it.";

    /// <summary>Heading of the list of what comes after the intro.</summary>
    public const string NextHeading = "What happens next";

    /// <summary>What comes after the intro.</summary>
    public static readonly IReadOnlyList<string> NextItems =
    [
        "Allow the camera when Windows asks.",
        "Look at each screen once.",
    ];

    /// <summary>The intro's pages, in order.</summary>
    public static readonly IReadOnlyList<IntroPage> IntroPages =
    [
        new(IntroArt.Scene, WelcomeTitle, WelcomeHeadline, "Fig. 1", SceneCaption, []),
        new(IntroArt.Moves, "Look. Settle. Type.", null, "Fig. 2", "Three moves, no clicking.", Moves),
        new(IntroArt.Privacy, "Your camera stays yours.", null, "Fig. 3", "Everything happens on this PC.", PrivacyLines),
        new(IntroArt.Tray, "Find Gander in the system tray", null, "Fig. 4", "Gander lives by the clock.", [TrayText]),
    ];

    /// <summary>The primary button on every intro page but the last.</summary>
    public const string IntroNextButton = "Next";

    /// <summary>The primary button on the last intro page (and the old welcome's "Get Started").</summary>
    public const string WelcomeButton = "Set Up Gander";

    /// <summary>The Back button.</summary>
    public const string IntroBackButton = "Back";

    /// <summary>The quiet button that leaves the intro early.</summary>
    public const string IntroSkipButton = "Skip intro";

    /// <summary>"Page 2 of 4" for assistive technology and the page dots.</summary>
    public static string IntroPageLine(int page) => $"Page {page + 1} of {SetupFlow.IntroPageCount}";

    /// <summary>The advice on the camera check.</summary>
    public const string CameraAdvice =
        "Put the camera where it sees your face from every monitor. On top of the middle monitor works best.";

    /// <summary>Shown when fewer than two monitors are connected.</summary>
    public const string NeedsSecondDisplay = "Connect a second monitor to calibrate.";

    /// <summary>The camera check with one monitor: setup ends here, and the tray menu asks once a second one is in.</summary>
    public const string CalibrateLater =
        "Connect a second monitor to calibrate. You can finish now: when one is connected, Gander's menu offers the calibration.";

    /// <summary>Shown after the user cancelled.</summary>
    public const string CalibrationCancelled = "Calibration cancelled.";

    /// <summary>Title of the "Needs you" step.</summary>
    public const string NeedsYouTitle = "Camera access";

    /// <summary>What to do when Windows blocks the camera for Gander (camera permission Denied).</summary>
    public const string CameraDeniedMessage =
        "Open Windows Settings, go to Privacy & security > Camera, and allow desktop apps to use the camera. "
        + "This window carries on by itself.";

    /// <summary>What to do when the camera is blocked by policy (camera permission Restricted).</summary>
    public const string CameraRestrictedMessage =
        "Camera access is turned off for this PC (for example by your organization). Ask the administrator, "
        + "or check Settings > Privacy & security > Camera. This window carries on by itself.";

    /// <summary>The Windows Settings page to open for the camera, as a URI the UI can launch.</summary>
    public const string CameraSettingsUri = "ms-settings:privacy-webcam";

    /// <summary>Title of a step, for the window header.</summary>
    public static string Title(SetupStep step) => step switch
    {
        SetupStep.Welcome => WelcomeTitle,
        SetupStep.NeedsYou => NeedsYouTitle,
        SetupStep.CameraCheck => "Check your camera",
        SetupStep.Calibrating => "Look at each monitor",
        _ => "Calibration result",
    };

    /// <summary>"Step 2 of 4"; null for the welcome, which is not numbered.</summary>
    public static string? StepLine(SetupStep step) =>
        step == SetupStep.Welcome ? null : $"Step {(int)step} of {SetupFlow.StepCount}";

    /// <summary>Prompt for the instruction while looking at a monitor: "Look at Left monitor".</summary>
    public static string LookAt(string displayName) => $"Look at {displayName}";

    /// <summary>What to tell the user to do after a run ended early.</summary>
    public const string TryAgain = "Calibrate again.";
}
