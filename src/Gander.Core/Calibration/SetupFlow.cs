namespace Gander.Core.Calibration;

/// <summary>The steps of the Set Up Gander window, in order.</summary>
public enum SetupStep
{
    /// <summary>First setup only: what Gander does and its privacy promise (SPEC F26). Not counted in "Step N of 4".</summary>
    Welcome = 0,

    /// <summary>"Needs you": Windows blocks the camera for Gander. Replaces the Mac camera and Accessibility steps.</summary>
    NeedsYou = 1,

    /// <summary>The live camera preview hint (<see cref="FramingAdvice"/>) and the Start button.</summary>
    CameraCheck = 2,

    /// <summary>The guided look at each monitor.</summary>
    Calibrating = 3,

    /// <summary>The result of the calibration.</summary>
    Done = 4,
}

/// <summary>
/// The pure state machine behind the Set Up Gander window (SPEC F20, CONTRACT 6.5.3): which step shows, decided by one
/// fact and by the user's moves. It performs no I/O. Port of the Mac <c>SetupFlow</c> with the macOS Accessibility
/// step gone and the camera permission step turned into "Needs you" (shown only while Windows blocks the camera).
/// <para>
/// Steps: <see cref="SetupStep.Welcome"/> (first setup only), <see cref="SetupStep.NeedsYou"/> (camera blocked),
/// <see cref="SetupStep.CameraCheck"/>, <see cref="SetupStep.Calibrating"/>, <see cref="SetupStep.Done"/>.
/// </para>
/// </summary>
public sealed class SetupFlow
{
    /// <summary>Number of numbered steps, for "Step 3 of 4".</summary>
    public const int StepCount = 4;

    /// <summary>Number of pages in the intro that the welcome step shows.</summary>
    public const int IntroPageCount = 4;

    /// <summary>Opens at the first step that applies to <paramref name="cameraAllowed"/>, or on the welcome step when <paramref name="showsWelcome"/> is set (first setup, empty library).</summary>
    public SetupFlow(bool cameraAllowed, bool showsWelcome = false)
    {
        Step = showsWelcome ? SetupStep.Welcome : FirstStep(cameraAllowed);
    }

    /// <summary>The step that shows.</summary>
    public SetupStep Step { get; private set; }

    /// <summary>The intro page that shows on the welcome step, 0 to <see cref="IntroPageCount"/> minus one.</summary>
    public int IntroPage { get; private set; }

    /// <summary>True on the last intro page, where the primary button sets Gander up instead of turning the page.</summary>
    public bool IsLastIntroPage => IntroPage == IntroPageCount - 1;

    /// <summary>True while the window waits for the user to allow the camera in Windows Settings.</summary>
    public bool IsPermissionStep => Step == SetupStep.NeedsYou;

    /// <summary>True while the camera must run for the window: the camera check and the calibration.</summary>
    public bool NeedsCamera => Step is SetupStep.CameraCheck or SetupStep.Calibrating;

    /// <summary>
    /// True while automatic switching holds so the user can move and look freely: the camera check and the calibration.
    /// The result step does not hold: while the window is in front "Gander in front" already does, and a window left
    /// behind other apps must not stop switching unseen.
    /// </summary>
    public bool HoldsTracking => Step is SetupStep.CameraCheck or SetupStep.Calibrating;

    /// <summary>The first step that applies: "Needs you" while the camera is blocked, otherwise the camera check.</summary>
    public static SetupStep FirstStep(bool cameraAllowed) => cameraAllowed ? SetupStep.CameraCheck : SetupStep.NeedsYou;

    /// <summary>
    /// Applies the camera fact. Allowing the camera moves the window on; blocking it moves it back to "Needs you"
    /// from the camera check. The calibration and the result never move on their own, and neither does the welcome.
    /// </summary>
    public void Update(bool cameraAllowed)
    {
        switch (Step)
        {
            case SetupStep.NeedsYou:
                if (cameraAllowed)
                {
                    Step = SetupStep.CameraCheck;
                }

                break;
            case SetupStep.CameraCheck:
                if (!cameraAllowed)
                {
                    Step = SetupStep.NeedsYou;
                }

                break;
            default:
                break;
        }
    }

    /// <summary>"Next" on the intro: the next page, or on the last page on to the first step that applies (as <see cref="GetStarted"/>).</summary>
    public void NextIntro(bool cameraAllowed)
    {
        if (Step != SetupStep.Welcome)
        {
            return;
        }

        if (IsLastIntroPage)
        {
            GetStarted(cameraAllowed);
        }
        else
        {
            IntroPage++;
        }
    }

    /// <summary>"Back" on the intro: the page before; nothing on the first page.</summary>
    public void BackIntro()
    {
        if (Step == SetupStep.Welcome && IntroPage > 0)
        {
            IntroPage--;
        }
    }

    /// <summary>"Skip intro": straight on to the first step that applies.</summary>
    public void SkipIntro(bool cameraAllowed) => GetStarted(cameraAllowed);

    /// <summary>"Get Started" on the welcome step: on to the first step that applies.</summary>
    public void GetStarted(bool cameraAllowed)
    {
        if (Step == SetupStep.Welcome)
        {
            Step = FirstStep(cameraAllowed);
        }
    }

    /// <summary>"Start calibration" on the camera check.</summary>
    public void StartCalibration()
    {
        if (Step == SetupStep.CameraCheck)
        {
            Step = SetupStep.Calibrating;
        }
    }

    /// <summary>The calibration ran to the end (its fit succeeded or failed; the result says which).</summary>
    public void CalibrationFinished()
    {
        if (Step == SetupStep.Calibrating)
        {
            Step = SetupStep.Done;
        }
    }

    /// <summary>Cancel, Esc, or an interrupted run: back to the camera check.</summary>
    public void CalibrationCancelled()
    {
        if (Step == SetupStep.Calibrating)
        {
            Step = SetupStep.CameraCheck;
        }
    }

    /// <summary>"Calibrate again" on the result.</summary>
    public void CalibrateAgain()
    {
        if (Step == SetupStep.Done)
        {
            Step = SetupStep.CameraCheck;
        }
    }
}
