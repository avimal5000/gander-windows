using System;
using System.Diagnostics;
using Sway.Core.Calibration;

namespace Sway.App.Hosting;

/// <summary>Opens the one Windows page Gander ever links to.</summary>
public static class ShellLauncher
{
    /// <summary>Opens Windows Settings at the camera privacy page. Quietly does nothing when Windows refuses.</summary>
    public static void OpenCameraSettings()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(SetupPresentation.CameraSettingsUri) { UseShellExecute = true }); // privacy-guard:allow opens the ms-settings camera privacy page
        }
        catch (Exception)
        {
            // Nothing else to do: the message already says where the setting is.
        }
    }
}
