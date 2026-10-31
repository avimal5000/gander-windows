namespace Sway.Core.Calibration;

/// <summary>Wording for the Set Up Sway window that more than one place needs (SPEC F20, F26), in Windows words.</summary>
public static class SetupPresentation
{
    /// <summary>Title of the welcome step.</summary>
    public const string WelcomeTitle = "Welcome to Sway";

    /// <summary>What Sway does.</summary>
    public const string WelcomeLine = "Look at a monitor, and Sway moves your typing there. The pointer never moves.";

    /// <summary>The privacy promise.</summary>
    public const string WelcomePrivacy =
        "Your camera stays private. Sway reads only which way your head is turned, on this PC. "
        + "Video is never recorded, saved, or sent anywhere.";

    /// <summary>The welcome step's button.</summary>
    public const string WelcomeButton = "Get Started";

    /// <summary>The advice on the camera check.</summary>
    public const string CameraAdvice =
        "Put the camera where it sees your face from every monitor. On top of the middle monitor works best.";

    /// <summary>Shown when fewer than two monitors are connected.</summary>
    public const string NeedsSecondDisplay = "Connect a second monitor to calibrate.";

    /// <summary>Shown after the user cancelled.</summary>
    public const string CalibrationCancelled = "Calibration cancelled.";

    /// <summary>Title of the "Needs you" step.</summary>
    public const string NeedsYouTitle = "Camera access";

    /// <summary>What to do when Windows blocks the camera for Sway (camera permission Denied).</summary>
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
