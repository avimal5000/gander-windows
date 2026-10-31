using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Sway.Core.Controller;
using Sway.Core.Platform;
using Sway.Core.Runtime;

namespace Sway.App.Presentation;

/// <summary>Copies the controller's state into a <see cref="DiagnosticsSnapshot"/>. Reads only; call it on the main thread.</summary>
public static class DiagnosticsSnapshotBuilder
{
    private static readonly string[] SettingKeys =
    [
        SettingsKeys.TrackingEnabled,
        SettingsKeys.CameraDeviceId,
        SettingsKeys.SwitchSensitivity,
        SettingsKeys.FocusLockShortcut,
        SettingsKeys.CalibrationLibrary,
    ];

    /// <summary>Builds the snapshot. <paramref name="scalePercent"/> answers the scale of a monitor (null if unknown).</summary>
    public static DiagnosticsSnapshot Build(
        TrackingController controller,
        ISettingsStore settings,
        Func<Sway.Core.DisplayInfo, int?> scalePercent,
        string swayVersion,
        string windowsVersion,
        string architecture,
        DateTimeOffset now)
    {
        var running = (controller.Status as CaptureStatus.Running)?.Device ?? controller.RunningDevice;
        var selected = controller.SelectedCameraId;
        var saved = new List<KeyValuePair<string, string>>();
        foreach (var key in SettingKeys)
        {
            if (settings.Read(key) is { } value)
            {
                saved.Add(new KeyValuePair<string, string>(key, value));
            }
        }

        var calibration = controller.ActiveCalibration;
        var sensitivity = controller.Sensitivity;
        return new DiagnosticsSnapshot
        {
            SwayVersion = swayVersion,
            WindowsVersion = windowsVersion,
            Architecture = architecture,
            StatusLine = controller.StatusLine,
            TrackingEnabled = controller.TrackingEnabled,
            FocusLocked = controller.FocusLocked,
            CaptureText = CaptureText.Summary(controller.Status),
            HoldText = controller.TrackingHold?.Text(),
            CurrentCamera = controller.CurrentCameraName,
            Cameras = controller.Devices
                .Select(device => new DiagnosticsCamera(
                    device.Name,
                    device.IsBuiltIn,
                    device.Id == selected,
                    running is not null && device.Id == running.Id))
                .ToList(),
            Displays = controller.Layout.Displays
                .Select(display => new DiagnosticsDisplay(
                    controller.DisplayName(display.Id),
                    (int)Math.Round(display.Frame.Width),
                    (int)Math.Round(display.Frame.Height),
                    scalePercent(display),
                    display.IsMain,
                    controller.TrackingFocus is { } focus && focus == display.Id))
                .ToList(),
            HasCalibration = calibration is not null,
            CalibrationCreatedAt = calibration?.Saved.CreatedAt,
            CalibrationPartial = calibration?.IsPartial ?? false,
            Now = now,
            LibraryCount = controller.Library.Profiles.Count,
            DwellSeconds = sensitivity.Dwell,
            CooldownSeconds = sensitivity.Cooldown,
            ConfidenceLevel = sensitivity.ConfidenceLevel,
            Settings = saved,
            SwitchLines = controller.SwitchLog.Reverse()
                .Select(entry => $"{entry.Time.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture)} {entry.Text}")
                .ToList(),
            LogLines = controller.LogLines.TakeLast(DiagnosticsText.MaxLogLines).ToList(),
        };
    }
}
