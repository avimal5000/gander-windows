namespace Gander.Core.Calibration;

/// <summary>When the onboarding walkthrough opens by itself. Pure.</summary>
public static class FirstRunRule
{
    /// <summary>
    /// True when the Set Up window should open at start-up: the walkthrough has not been shown yet and nothing is
    /// calibrated. A user who already has a calibration (including one carried over from the earlier name) is never
    /// first-run, whatever the flag says.
    /// </summary>
    public static bool ShouldOpenSetup(bool onboardingSeen, int calibrationCount) =>
        !onboardingSeen && calibrationCount == 0;
}
