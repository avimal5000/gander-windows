using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Gander.Core.Controller;
using Gander.Core.Platform;
using Gander.Core.Runtime;

namespace Gander.App.Presentation;

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
        Func<Gander.Core.DisplayInfo, int?> scalePercent,
        string ganderVersion,
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
            GanderVersion = ganderVersion,
            WindowsVersion = windowsVersion,
            Architecture = architecture,
            StatusLine = controller.StatusLine,
            TrackingEnabled = controller.TrackingEnabled,
            FocusLocked = controller.FocusLocked,
            CaptureText = CaptureText.Summary(controller.Status),
            HoldText = controller.TrackingHold?.Text(),
            Switching = BuildSwitching(controller, now),
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

    private static DiagnosticsSwitching BuildSwitching(TrackingController controller, DateTimeOffset now)
    {
        var inputs = controller.CurrentHoldInputs;
        var counts = controller.Diagnostics;
        var calibration = controller.ActiveCalibration;
        var decision = controller.LastDecision;
        var classification = decision?.Classification;
        var targets = controller.TrackedTargets ?? calibration?.Profile.DisplayIds;
        var decisionText = classification is null
            ? string.Empty
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{classification.Kind}{(classification.Best is { } best ? $", best {controller.DisplayName(best)}" : string.Empty)}, margin {classification.Margin:0.00}{(classification.RunnerUp is { } second ? $", runner-up {controller.DisplayName(second)}" : string.Empty)}");
        return new DiagnosticsSwitching
        {
            IsTrackingActive = controller.IsTrackingActive,
            TrackingEnabled = controller.TrackingEnabled,
            CalibrationRunning = controller.CalibrationRunning,
            IsCameraActive = controller.IsCameraActive,
            CaptureText = CaptureText.Summary(controller.Status),
            HasActiveCalibration = calibration is not null,
            CalibrationCamera = calibration?.Saved.CameraName,
            CalibrationFingerprint = calibration?.Saved.LayoutFingerprint,
            LiveFingerprint = controller.Layout.Fingerprint,
            HoldText = controller.TrackingHold?.Text(),
            FocusLocked = inputs.FocusLocked,
            GanderInFront = inputs.GanderInFront,
            DisplaysChanging = inputs.DisplaysChanging,
            DesktopChanging = inputs.SpaceChanging,
            SwitchingInFlight = inputs.Switching,
            BelievedFocus = controller.TrackingFocus is { } focus ? $"{focus} ({controller.DisplayName(focus)})" : null,
            Targets = targets?.Select(id => $"{id} ({controller.DisplayName(id)})").ToList() ?? [],
            HasDecision = decision is not null,
            DecisionText = decisionText,
            PhaseName = decision is null ? string.Empty : TrackingController.PhaseName(decision.Phase),
            ReasonText = decision?.Reason?.ToString(),
            SwitchToSet = decision?.SwitchTo is not null,
            EnterMargin = controller.EnterMargin,
            HasPose = counts.LastPose is not null,
            Yaw = counts.LastPose?.Yaw ?? 0,
            Pitch = counts.LastPose?.Pitch ?? 0,
            Roll = counts.LastPose?.Roll ?? 0,
            FaceConfidence = counts.LastPose?.FaceConfidence ?? 0,
            PoseAgeSeconds = counts.LastPoseAt is { } at ? Math.Max(0, (now - at).TotalSeconds) : null,
            LastSampleHadFace = counts.LastSampleAt is null ? null : counts.LastSampleHadFace,
            SamplesReceived = counts.SamplesReceived,
            SamplesWithFace = counts.SamplesWithFace,
            SamplesProcessed = counts.SamplesProcessed,
            SwitchRequests = counts.SwitchRequests,
            Focused = counts.Focused,
            NoWindow = counts.NoWindow,
            Failed = counts.Failed,
            SkippedByHold = counts.SkippedByHold,
            SkippedInFlight = counts.SkippedInFlight,
        };
    }
}
