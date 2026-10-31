using System.Text.Json;
using Sway.Core.Controller;
using Sway.Core.Platform;
using Sway.Core.Runtime;

namespace Sway.Core.Tests.Controller;

/// <summary>Port of the Mac AppModelTrackingSwitchTests ("Tracking switch, launch state, and sensitivity").</summary>
public class TrackingSwitchTests
{
    private const string TrackingKey = SettingsKeys.TrackingEnabled;

    // MARK: The saved choice

    [Fact]
    public void TrackingIsOffByDefault()
    {
        using var h = Harness.Make(calibrated: true);

        Assert.False(h.Controller.TrackingEnabled);
        Assert.Equal(TrackingStatus.Paused, h.Controller.TrackingStatus);
        Assert.Equal(TrayIconState.Paused, h.Controller.TrayIcon);
        Assert.False(h.Settings.Values.ContainsKey(TrackingKey));
    }

    [Fact]
    public async Task TheUsersSwitchWritesTheSavedChoice()
    {
        using var h = Harness.Make(calibrated: true);

        await h.SetTracking(true);
        Assert.Equal("true", h.Settings.Values[TrackingKey]);
        await h.SetTracking(false);
        Assert.Equal("false", h.Settings.Values[TrackingKey]);
    }

    [Fact]
    public async Task TracksWithoutACalibrationStillSavesTheChoice()
    {
        using var h = Harness.Make();

        await h.SetTracking(true);

        Assert.Equal("true", h.Settings.Values[TrackingKey]);
        Assert.Equal(0, h.Source.StartCount); // Nothing fits, so the camera stays off.
    }

    // MARK: Launch

    [Fact]
    public void LaunchingWithTrackingSavedOnStartsTheCameraWithoutAnyWindow()
    {
        using var h = Harness.Make(autoRun: true, calibrated: true, trackingSaved: true);

        Assert.True(h.Controller.TrackingEnabled);
        Assert.Equal(1, h.Source.StartCount);
        Assert.IsType<CaptureStatus.Running>(h.Controller.Status);
        Assert.Equal([new PoseSourceConfiguration(null, SwayPolicy.DefaultFrameRate, false)], h.Source.Configurations);
        Assert.Equal(TrackingStatus.NoFace, h.Controller.TrackingStatus);
    }

    [Fact]
    public void TheCalibrationIsLookedUpForTheCameraCaptureActuallyOpens()
    {
        // No camera chosen, and capture opens the external camera (the system default), not the built-in one. Only the
        // external camera is calibrated.
        using var h = Harness.Make(
            calibrated: true, calibrationCameras: [Fixtures.ExternalCamera.Name], capturePicks: Fixtures.ExternalCamera, trackingSaved: true);
        h.Cameras.Devices = [Fixtures.ExternalCamera];
        h.Controller.RefreshEnvironment();
        h.Drain();

        Assert.Equal(Fixtures.ExternalCamera, ((CaptureStatus.Running)h.Controller.Status).Device);
        Assert.Equal(1, h.Source.StartCount);
        Assert.Equal(0, h.Source.StopCount); // No start-stop loop between the guessed and the running camera.

        // A screen lock suspends capture; the calibration still fits, so the session stays and resumes by itself.
        h.RaiseSession(SessionEvent.ScreenLocked);
        Assert.IsType<CaptureStatus.Suspended>(h.Controller.Status);
        Assert.NotNull(h.Controller.ActiveCalibration);
        Assert.Equal(1, h.Source.StartCount);
    }

    [Fact]
    public async Task ACameraConnectingLaterIsNoticed()
    {
        using var h = Harness.Make(calibrated: true, calibrationCameras: [Fixtures.ExternalCamera.Name]);
        h.Cameras.Devices = [Fixtures.BuiltInCamera];
        h.Controller.RefreshEnvironment();
        await h.Controller.SelectCameraAsync(Fixtures.ExternalCamera.Id);
        h.Drain();
        Assert.Null(h.Controller.ActiveCalibration); // The chosen camera is not connected yet.

        h.Cameras.Plug(Fixtures.ExternalCamera);
        h.Drain();

        // The device list is read 0.5 s after the last notification, not on each one (Mac CONTRACT 6.5.5 item 5).
        Assert.Null(h.Controller.ActiveCalibration);
        h.Advance(TrackingController.EnvironmentRefreshDelay);
        Assert.Equal(Fixtures.ExternalCamera.Name, h.Controller.PredictedCameraName);
        Assert.NotNull(h.Controller.ActiveCalibration);
    }

    [Fact]
    public void LaunchingWithTrackingSavedOnButNothingCalibratedKeepsTheCameraOff()
    {
        using var h = Harness.Make(autoRun: true, trackingSaved: true);

        Assert.True(h.Controller.TrackingEnabled);
        Assert.Equal(0, h.Source.StartCount);
        Assert.Equal(TrackingStatusKind.NeedsCalibration, h.Controller.TrackingStatus.Kind);
    }

    [Fact]
    public void LaunchingWithTrackingOffDoesNothing()
    {
        using var h = Harness.Make(autoRun: true, calibrated: true);

        Assert.Equal(0, h.Source.StartCount);
        Assert.Equal(0, h.Main.PostCount);
    }

    // MARK: Automatic changes and quitting never change the saved choice

    [Fact]
    public async Task ACameraFailureAtStartTurnsTrackingOffInMemoryOnly()
    {
        using var h = Harness.Make(calibrated: true);
        h.Source.StartError = new CameraError(CameraErrorKind.NoDevice);

        await h.SetTracking(true);

        Assert.False(h.Controller.TrackingEnabled);
        Assert.Equal(TrackingStatusKind.CameraFailed, h.Controller.TrackingStatus.Kind);
        Assert.Equal("true", h.Settings.Values[TrackingKey]); // The user's choice is still on.
        using var relaunched = h.Relaunch();
        Assert.True(relaunched.Controller.TrackingEnabled);
    }

    [Fact]
    public async Task QuittingLeavesTheSavedChoiceSoTheNextLaunchStartsTracking()
    {
        using var h = Harness.Make(autoRun: true, calibrated: true);
        await h.SetTracking(true);
        Assert.IsType<CaptureStatus.Running>(h.Controller.Status);

        await h.Controller.ShutdownAsync();
        h.Drain();

        Assert.False(h.Controller.TrackingEnabled); // Off in memory.
        Assert.Equal("true", h.Settings.Values[TrackingKey]); // But still the user's choice.
        Assert.False(h.Controller.IsCameraActive);
        using var relaunched = h.Relaunch(autoRun: true);
        Assert.True(relaunched.Controller.TrackingEnabled);
        Assert.Equal(1, relaunched.Source.StartCount); // The new controller starts tracking.
        Assert.IsType<CaptureStatus.Running>(relaunched.Controller.Status);
    }

    [Fact]
    public async Task QuittingAfterThePauseSwitchKeepsItPaused()
    {
        using var h = Harness.Make(autoRun: true, calibrated: true);
        await h.SetTracking(true);
        await h.SetTracking(false);

        await h.Controller.ShutdownAsync();

        Assert.Equal("false", h.Settings.Values[TrackingKey]);
        using var relaunched = h.Relaunch();
        Assert.False(relaunched.Controller.TrackingEnabled);
    }

    // MARK: Pausing

    [Fact]
    public async Task PausingTurnsTheCameraOffAndTheStatusToPaused()
    {
        using var h = Harness.Make(autoRun: true, calibrated: true);
        await h.SetTracking(true);
        Assert.IsType<CaptureStatus.Running>(h.Controller.Status);

        await h.SetTracking(false);

        Assert.False(h.Controller.IsCameraActive);
        Assert.True(h.Source.StopCount >= 1);
        Assert.Equal(TrackingStatus.Paused, h.Controller.TrackingStatus);
        Assert.Equal("Paused", h.Controller.StatusLine);
    }

    [Fact]
    public async Task ACalibrationRunningWhilePausedShowsCalibratingAndReturnsToPaused()
    {
        using var h = Harness.Make(autoRun: true);

        await h.Controller.SetCalibrationRunningAsync(true);
        h.Drain();
        Assert.Equal(TrackingStatusKind.Calibrating, h.Controller.TrackingStatus.Kind);
        Assert.Equal(1, h.Source.StartCount);

        await h.Controller.SetCalibrationRunningAsync(false);
        h.Drain();
        Assert.Equal(TrackingStatus.Paused, h.Controller.TrackingStatus);
        Assert.False(h.Controller.IsCameraActive);
    }

    // MARK: Sensitivity (SPEC D31)

    [Fact]
    public void TheDefaultSensitivityGivesTheEngineDefaults()
    {
        using var h = Harness.Make(calibrated: true);

        Assert.Equal(SwitchSensitivity.Default, h.Controller.Sensitivity);
        Assert.Equal(SwitchSettings.Default, h.Engine.LastRecorder!.Settings);
    }

    [Fact]
    public void ChangingTheSensitivitySavesItAndRebuildsTheDeciderWithTheMappedSettings()
    {
        using var h = Harness.Make(calibrated: true);
        var recorders = h.Engine.Recorders.Count;

        h.Controller.SetSensitivity(new SwitchSensitivity(0.8, 4));

        var settings = h.Engine.LastRecorder!.Settings;
        Assert.Equal(recorders + 1, h.Engine.Recorders.Count);
        Assert.Equal(0.8, settings.Dwell);
        Assert.Equal(0.45, settings.EnterMargin);
        Assert.Equal(0.225, settings.StayMargin);
        Assert.Equal(new SwitchSensitivity(0.8, 4), h.Controller.Sensitivity);
        Assert.True(h.Settings.Values.ContainsKey(SettingsKeys.SwitchSensitivity));
        using var relaunched = h.Relaunch();
        Assert.Equal(new SwitchSensitivity(0.8, 4), relaunched.Controller.Sensitivity);
    }

    [Fact]
    public void TheSameSensitivityAgainRebuildsNothing()
    {
        using var h = Harness.Make(calibrated: true);
        var recorders = h.Engine.Recorders.Count;

        h.Controller.SetSensitivity(SwitchSensitivity.Default);

        Assert.Equal(recorders, h.Engine.Recorders.Count);
        Assert.False(h.Settings.Values.ContainsKey(SettingsKeys.SwitchSensitivity));
    }

    [Fact]
    public void ChangingTheSensitivityAppliesTheCooldownToTheDecider()
    {
        using var h = Harness.Make(calibrated: true);

        h.Controller.SetSensitivity(new SwitchSensitivity(0.1, 0.3, 1));

        var settings = h.Engine.LastRecorder!.Settings;
        Assert.Equal(0.1, settings.Dwell);
        Assert.Equal(0.3, settings.Cooldown);
        Assert.Equal(0.225, settings.EnterMargin);
        Assert.Equal(SwitchSettings.Default.MaxGap, settings.MaxGap);
        using var relaunched = h.Relaunch();
        Assert.Equal(new SwitchSensitivity(0.1, 0.3, 1), relaunched.Controller.Sensitivity);
    }

    [Fact]
    public void OutOfRangeSensitivityIsClamped()
    {
        using var h = Harness.Make(calibrated: true);

        h.Controller.SetSensitivity(new SwitchSensitivity(99, 99, -3));

        Assert.Equal(SwitchSensitivity.DwellRange.Upper, h.Controller.Sensitivity.Dwell);
        Assert.Equal(SwitchSensitivity.CooldownRange.Upper, h.Controller.Sensitivity.Cooldown);
        Assert.Equal(0, h.Controller.Sensitivity.ConfidenceLevel);
    }

    [Fact]
    public void ASensitivityMadeWithTheFieldsSetDirectlyIsClampedToo()
    {
        using var h = Harness.Make(calibrated: true);
        var wild = SwitchSensitivity.Default with { Dwell = -4, Cooldown = 0, ConfidenceLevel = 99 };

        h.Controller.SetSensitivity(wild);

        Assert.Equal(SwitchSensitivity.DwellRange.Lower, h.Controller.Sensitivity.Dwell);
        Assert.Equal(SwitchSensitivity.CooldownRange.Lower, h.Controller.Sensitivity.Cooldown);
        Assert.Equal(SwitchSensitivity.ConfidenceMargins.Count - 1, h.Controller.Sensitivity.ConfidenceLevel);
    }

    [Fact]
    public void StoredSensitivityOutsideTheRangesIsClampedAndGarbageIsIgnored()
    {
        using var h = Harness.Make();
        h.Settings.Values[SettingsKeys.SwitchSensitivity] = """{"dwell": 50, "cooldown": 50, "confidenceLevel": 9}""";
        using (var relaunched = h.Relaunch())
        {
            var clamped = relaunched.Controller.Sensitivity;
            Assert.Equal(SwitchSensitivity.DwellRange.Upper, clamped.Dwell);
            Assert.Equal(SwitchSensitivity.CooldownRange.Upper, clamped.Cooldown);
            Assert.Equal(SwitchSensitivity.ConfidenceMargins.Count - 1, clamped.ConfidenceLevel);
        }

        h.Settings.Values[SettingsKeys.SwitchSensitivity] = "nope";
        using var again = h.Relaunch();
        Assert.Equal(SwitchSensitivity.Default, again.Controller.Sensitivity);
    }

    [Fact]
    public void SettingsSavedBeforeThePauseExistedLoadWithTheDefaultPause()
    {
        using var h = Harness.Make(calibrated: true);
        h.Settings.Values[SettingsKeys.SwitchSensitivity] = """{"dwell":0.2,"confidenceLevel":2}""";

        using var relaunched = h.Relaunch();

        Assert.Equal(new SwitchSensitivity(0.2, 0.75, 2), relaunched.Controller.Sensitivity);
        Assert.Equal(SwitchSettings.Default.Cooldown, relaunched.Controller.Sensitivity.Cooldown);
        Assert.Equal(0.2, relaunched.Controller.Sensitivity.ToSwitchSettings().Dwell);
        Assert.Equal(SwitchSettings.Default.EnterMargin, relaunched.Controller.EnterMargin);
    }

    [Fact]
    public void SavedSettingsWithoutAPauseAreRewrittenWithOneOnTheNextChange()
    {
        using var h = Harness.Make(calibrated: true);
        h.Settings.Values[SettingsKeys.SwitchSensitivity] = """{"dwell":0.2,"confidenceLevel":2}""";
        using var relaunched = h.Relaunch();

        relaunched.Controller.SetSwitchConfidence(3);

        using var saved = JsonDocument.Parse(h.Settings.Values[SettingsKeys.SwitchSensitivity]);
        Assert.Equal(0.2, saved.RootElement.GetProperty("dwell").GetDouble());
        Assert.Equal(0.75, saved.RootElement.GetProperty("cooldown").GetDouble());
        Assert.Equal(3, saved.RootElement.GetProperty("confidenceLevel").GetInt32());
    }

    [Fact]
    public void TheEnterMarginReachesTheLoopsLookCheck()
    {
        using var h = Harness.Make(calibrated: true);
        h.Controller.SetSensitivity(new SwitchSensitivity(0.4, 4));
        Assert.Equal(0.45, h.Controller.EnterMargin);

        h.Controller.UseInstantTiming();
        Assert.Equal(0.45, h.Controller.EnterMargin); // The preset leaves the confidence alone.
        Assert.Equal(SwitchSensitivity.Instant.Dwell, h.Controller.Sensitivity.Dwell);
        Assert.Equal(SwitchSensitivity.Instant.Cooldown, h.Controller.Sensitivity.Cooldown);
    }

    [Fact]
    public void TheHoldTimeAndPauseSnapToTheGridAndRestoreDefaultsGoesBack()
    {
        using var h = Harness.Make(calibrated: true);

        h.Controller.SetHoldTime(0.52);
        h.Controller.SetCooldown(0.77);
        Assert.Equal(0.5, h.Controller.Sensitivity.Dwell, 9);
        Assert.Equal(0.75, h.Controller.Sensitivity.Cooldown, 9);

        h.Controller.RestoreSensitivityDefaults();
        Assert.Equal(SwitchSensitivity.Default, h.Controller.Sensitivity);
    }
}

