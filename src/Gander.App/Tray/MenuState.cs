using Gander.App.Presentation;
using Gander.Core.Controller;

namespace Gander.App.Tray;

/// <summary>Copies what the tray menu and the Gander window's home page show from the controller.</summary>
public static class MenuState
{
    /// <summary>The state now. <paramref name="setupCompleted"/> is whether the onboarding was finished once.</summary>
    public static TrayMenuInput Read(TrackingController controller, bool setupCompleted)
    {
        var facts = new TrayBannerFacts(
            controller.PermissionPrompt,
            controller.Library.Profiles.Count == 0,
            controller.TrackingEnabled,
            controller.Layout.Displays.Count,
            controller.ActiveCalibration is not null,
            controller.MenuShortcutMessage,
            SetupCompleted: setupCompleted);
        return new TrayMenuInput(
            controller.StatusLine,
            controller.TrackingStatus.Tone,
            controller.TrackingEnabled,
            controller.FocusLocked,
            controller.FocusLockShortcut,
            TrayBanner.Current(facts),
            LookSummary.Text(controller.IsCameraActive, controller.LastDecision?.Classification, controller.DisplayName),
            controller.Devices,
            controller.SelectedCameraId);
    }
}
