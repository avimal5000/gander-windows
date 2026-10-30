using Sway.Core.Controller;
using Sway.Core.Platform;
using Sway.Core.Runtime;

namespace Sway.Core.Tests.Controller;

/// <summary>
/// Port of the Mac AppModelCameraTests ("AppModel camera and pose"). Not ported (macOS only): the permission explanation
/// flow (continue, not now, system prompt, pending intent), the privacy-settings URL, the NSApplication terminate
/// notification, the window-request bookkeeping and the text readout throttling (UI).
/// </summary>
public class CameraTests
{
    private static CameraError Runtime(string detail) => new(CameraErrorKind.RuntimeFailure, detail);

    // MARK: Start and stop

    [Fact]
    public async Task TurningTrackingOnStartsThePoseSourceAndOffStopsIt()
    {
        using var h = Harness.Make(autoRun: true, calibrated: true);

        await h.SetTracking(true);
        Assert.True(h.Controller.TrackingEnabled);
        Assert.Equal(1, h.Source.StartCount);
        Assert.IsType<CaptureStatus.Running>(h.Controller.Status);
        Assert.Equal("No face · Integrated Camera", h.Controller.StatusLine);
        Assert.Equal(TrackingStatusKind.NoFace, h.Controller.TrackingStatus.Kind);

        await h.SetTracking(false);
        Assert.False(h.Controller.TrackingEnabled);
        Assert.True(h.Source.StopCount >= 1);
        Assert.IsType<CaptureStatus.Stopped>(h.Controller.Status);
        Assert.False(h.Controller.HasFace);
        Assert.Equal(TrayIconState.Paused, h.Controller.TrayIcon);
        Assert.False(h.Controller.IsCameraActive);
    }

    [Fact]
    public async Task TheConfigurationCarriesTheSelectedCameraAndDefaultFrameRate()
    {
        using var h = Harness.Make(
            autoRun: true, savedCameraId: Fixtures.ExternalCamera.Id, calibrated: true, calibrationCameras: [Fixtures.ExternalCamera.Name]);

        await h.SetTracking(true);

        Assert.Equal([new PoseSourceConfiguration(Fixtures.ExternalCamera.Id, SwayPolicy.DefaultFrameRate, false)], h.Source.Configurations);
    }

    [Fact]
    public void NothingStartsUntilSomethingAsksForTheCamera()
    {
        using var h = Harness.Make(autoRun: true, calibrated: true);

        Assert.Equal(0, h.Source.StartCount);
        Assert.IsType<CaptureStatus.Stopped>(h.Controller.Status);
        Assert.False(h.Controller.TrackingEnabled);
    }

    [Fact]
    public async Task QueuedCameraWorkEvaluatesTheLatestRequestWhenItRuns()
    {
        using var h = Harness.Make(autoRun: true, calibrated: true);

        var first = h.Controller.SetTrackingEnabledAsync(true);
        var second = h.Controller.SetTrackingEnabledAsync(false);
        var third = h.Controller.SetTrackingEnabledAsync(true);
        await first;
        await second;
        await third;
        h.Drain();

        // Unlike the Mac, each call begins its work at once (there is no main-actor hop to batch them), so the first start
        // is not coalesced away; the chain still ends in the state the last request asked for.
        Assert.True(h.Controller.TrackingEnabled);
        Assert.True(h.Controller.IsCameraActive);
        Assert.True(h.Source.IsStreaming);
        Assert.Equal(h.Source.StartCount - 1, h.Source.StopCount);
    }

    // MARK: Events

    [Fact]
    public async Task SamplesUpdateTheFaceState()
    {
        using var h = Harness.Make(calibrated: true);
        await h.StartTracking();

        for (var i = 0; i < 60; i++)
        {
            h.Feed(i / 15.0, yaw: i);
        }

        Assert.True(h.Controller.HasFace);
        Assert.Equal(TrackingStatusKind.Tracking, h.Controller.TrackingStatus.Kind);

        // "No face" shows only after half a second without one (Mac CONTRACT 6.5.5 item 8); the last face was at 3.93 s.
        h.Feed(4.1, face: false);
        Assert.True(h.Controller.HasFace);
        Assert.Equal(TrackingStatusKind.Tracking, h.Controller.TrackingStatus.Kind);
        h.Feed(4.5, face: false);
        Assert.False(h.Controller.HasFace);
        Assert.Equal(TrackingStatusKind.NoFace, h.Controller.TrackingStatus.Kind);
    }

    [Fact]
    public async Task EventsFlowFromTheStreamIntoTheController()
    {
        using var h = Harness.Make(autoRun: true, calibrated: true);
        await h.SetTracking(true);
        Assert.IsType<CaptureStatus.Running>(h.Controller.Status);
        Assert.False(h.Controller.HasFace);

        h.Feed(1, yaw: 12);

        Assert.True(h.Controller.HasFace);
        Assert.Single(h.Engine.LastRecorder!.Samples);
        Assert.Equal(12, h.Engine.LastRecorder.Samples[0].Pose!.Value.Yaw);
    }

    [Fact]
    public async Task LeavingTheRunningStateClearsTheLiveNumbers()
    {
        using var h = Harness.Make(calibrated: true);
        await h.StartTracking();
        h.Feed(1);
        h.Feed(1.1);
        Assert.True(h.Controller.HasFace);

        h.ReportStatus(new CaptureStatus.Suspended(SuspendReason.ScreenLocked));
        Assert.False(h.Controller.HasFace);
        Assert.Equal("Suspended: screen locked", h.Controller.StatusLine);
        Assert.Equal(TrayIconState.Paused, h.Controller.TrayIcon);

        h.ReportStatus(new CaptureStatus.Interrupted("Camera disconnected"));
        Assert.Equal(TrayIconState.Attention, h.Controller.TrayIcon);
    }

    [Fact]
    public async Task AFailedStatusSwitchesTrackingOffInMemoryAndKeepsTheSavedChoice()
    {
        using var h = Harness.Make(autoRun: true, calibrated: true);
        await h.SetTracking(true);

        h.ReportStatus(new CaptureStatus.Failed(Runtime("sensor died")));
        h.Drain();

        Assert.False(h.Controller.TrackingEnabled);
        Assert.True(h.Source.StopCount >= 1);
        Assert.Equal(new CaptureStatus.Failed(Runtime("sensor died")), h.Controller.Status);
        Assert.Equal("Camera unavailable", h.Controller.StatusLine);
        Assert.Equal(TrayIconState.Attention, h.Controller.TrayIcon);
        Assert.Equal("true", h.Settings.Values[SettingsKeys.TrackingEnabled]); // The next launch tries again.
    }

    [Fact]
    public async Task TheRealCamerasRecoverableStatusesKeepTrackingOn()
    {
        // What the live pose source reports for sleep, a stall and an unplug, each followed by recovery. None of these may
        // switch Tracking off or restart the source; only Failed does. (Unplug while the camera is still listed here.)
        using var h = Harness.Make(calibrated: true);
        var camera = Fixtures.BuiltInCamera;
        await h.Controller.SetTrackingEnabledAsync(true);
        h.Drain();

        CaptureStatus[] statuses =
        [
            new CaptureStatus.Starting(), new CaptureStatus.Running(camera), new CaptureStatus.Running(camera),
            new CaptureStatus.Suspended(SuspendReason.SystemSleep), new CaptureStatus.Starting(), new CaptureStatus.Running(camera),
            new CaptureStatus.Interrupted("No frames from the camera"), new CaptureStatus.Running(camera),
            new CaptureStatus.Interrupted("Camera disconnected"), new CaptureStatus.Starting(), new CaptureStatus.Running(camera),
        ];
        foreach (var status in statuses)
        {
            h.Source.EmitStatus(status);
        }

        h.Drain();

        Assert.Equal(new CaptureStatus.Running(camera), h.Controller.Status);
        Assert.True(h.Controller.TrackingEnabled);
        Assert.True(h.Controller.IsCameraActive);
        Assert.Equal(1, h.Source.StartCount);
        Assert.Equal(0, h.Source.StopCount);
    }

    [Fact]
    public async Task ALateStoppedFromTheStoppedStreamDoesNotHideTheFailure()
    {
        using var h = Harness.Make(autoRun: true, calibrated: true);
        await h.SetTracking(true);

        h.ReportStatus(new CaptureStatus.Failed(Runtime("sensor died")));
        h.Drain();
        Assert.False(h.Controller.IsCameraActive);

        h.Emit(new PoseSourceEvent.Status(new CaptureStatus.Stopped()));
        h.Feed(1);

        Assert.Equal(new CaptureStatus.Failed(Runtime("sensor died")), h.Controller.Status);
        Assert.False(h.Controller.HasFace);
    }

    [Fact]
    public async Task EventsFromTheStreamOfAnEarlierRunAreIgnoredAfterARestart()
    {
        using var h = Harness.Make(
            autoRun: true, calibrated: true, calibrationCameras: [Fixtures.BuiltInCamera.Name, Fixtures.ExternalCamera.Name]);
        await h.SetTracking(true);
        var first = h.Source;
        Assert.IsType<CaptureStatus.Running>(h.Controller.Status);

        await h.Controller.SelectCameraAsync(Fixtures.ExternalCamera.Id);
        h.Drain();
        Assert.Equal(2, first.StartCount);

        // The old stream was completed by StopAsync; a status on the new stream applies.
        h.ReportStatus(new CaptureStatus.Interrupted("Camera disconnected"));
        Assert.Equal(new CaptureStatus.Interrupted("Camera disconnected"), h.Controller.Status);
        Assert.True(h.Controller.TrackingEnabled);
    }

    [Fact]
    public async Task AStreamThatEndsOnItsOwnTurnsTrackingOffInMemory()
    {
        using var h = Harness.Make(autoRun: true, calibrated: true);
        await h.SetTracking(true);

        h.Source.FinishStream();
        h.Drain();

        Assert.False(h.Controller.TrackingEnabled);
        Assert.False(h.Controller.IsCameraActive);
        Assert.IsType<CaptureStatus.Stopped>(h.Controller.Status);
        Assert.Equal("true", h.Settings.Values[SettingsKeys.TrackingEnabled]);
    }

    // MARK: Camera selection

    [Fact]
    public async Task TheSelectedCameraIsPersistedAndRestored()
    {
        using var h = Harness.Make(calibrated: true);
        Assert.Null(h.Controller.SelectedCameraId);

        await h.Controller.SelectCameraAsync(Fixtures.ExternalCamera.Id);
        Assert.Equal(Fixtures.ExternalCamera.Id, h.Settings.Values[SettingsKeys.CameraDeviceId]);
        using (var relaunched = h.Relaunch())
        {
            Assert.Equal(Fixtures.ExternalCamera.Id, relaunched.Controller.SelectedCameraId);
        }

        await h.Controller.SelectCameraAsync(null);
        Assert.False(h.Settings.Values.ContainsKey(SettingsKeys.CameraDeviceId));
        using var again = h.Relaunch();
        Assert.Null(again.Controller.SelectedCameraId);
    }

    [Fact]
    public async Task ChangingTheCameraWhileRunningRestartsOnTheNewDevice()
    {
        using var h = Harness.Make(
            autoRun: true, calibrated: true, calibrationCameras: [Fixtures.BuiltInCamera.Name, Fixtures.ExternalCamera.Name]);
        await h.SetTracking(true);

        await h.Controller.SelectCameraAsync(Fixtures.ExternalCamera.Id);
        h.Drain();

        Assert.Equal([null, Fixtures.ExternalCamera.Id], h.Source.Configurations.Select(c => c.CameraDeviceId));
        Assert.True(h.Source.StopCount >= 1);
        Assert.True(h.Controller.IsCameraActive);
        Assert.Equal(new CaptureStatus.Running(Fixtures.ExternalCamera), h.Controller.Status);
    }

    [Fact]
    public async Task ChangingTheCameraWhileOffDoesNotStartIt()
    {
        using var h = Harness.Make(autoRun: true, calibrated: true);

        await h.Controller.SelectCameraAsync(Fixtures.ExternalCamera.Id);
        h.Drain();

        Assert.Equal(0, h.Source.StartCount);
    }

    [Fact]
    public void RefreshingReadsDevicesAndPermission()
    {
        using var h = Harness.Make(authorization: CameraAuthorization.NotDetermined, calibrated: true);
        Assert.Equal([Fixtures.BuiltInCamera.Id, Fixtures.ExternalCamera.Id], h.Controller.Devices.Select(d => d.Id));

        h.Cameras.Devices = [Fixtures.BuiltInCamera];
        h.Cameras.AuthorizationValue = CameraAuthorization.Authorized;
        h.Controller.RefreshEnvironment();

        Assert.Equal([Fixtures.BuiltInCamera.Id], h.Controller.Devices.Select(d => d.Id));
        Assert.Equal(CameraAuthorization.Authorized, h.Controller.Authorization);
    }

    // MARK: Permission

    [Fact]
    public async Task DeniedAccessShowsInstructionsAndKeepsTrackingOnBlocked()
    {
        using var h = Harness.Make(authorization: CameraAuthorization.Denied, calibrated: true);

        await h.SetTracking(true);

        Assert.Equal(CameraPermissionPrompt.Denied, h.Controller.PermissionPrompt);
        Assert.Equal(0, h.Source.StartCount);
        Assert.True(h.Controller.TrackingEnabled);
        Assert.Equal(TrackingStatusKind.CameraBlocked, h.Controller.TrackingStatus.Kind);
        Assert.Equal("Camera access needed", h.Controller.StatusLine);
    }

    [Fact]
    public async Task RestrictedAccessCannotBeFixedHere()
    {
        using var h = Harness.Make(authorization: CameraAuthorization.Restricted, calibrated: true);

        await h.SetTracking(true);

        Assert.Equal(CameraPermissionPrompt.Restricted, h.Controller.PermissionPrompt);
        Assert.Equal(0, h.Source.StartCount);
    }

    [Fact]
    public async Task ANotAuthorizedErrorWithAStaleAuthorizedStatusStillShowsInstructions()
    {
        using var h = Harness.Make(authorization: CameraAuthorization.Authorized, calibrated: true);
        h.Source.StartError = new CameraError(CameraErrorKind.NotAuthorized);

        await h.SetTracking(true);

        Assert.Equal(1, h.Source.StartCount);
        Assert.Equal(CameraPermissionPrompt.Denied, h.Controller.PermissionPrompt);
        Assert.True(h.Controller.TrackingEnabled);
    }

    [Fact]
    public async Task NotDeterminedIsTriedAndTheSourceDecides()
    {
        // Windows has no up-front prompt: the source reports NotAuthorized when the OS blocks the camera.
        using var h = Harness.Make(authorization: CameraAuthorization.NotDetermined, calibrated: true, autoRun: true);

        await h.SetTracking(true);

        Assert.Equal(1, h.Source.StartCount);
        Assert.Null(h.Controller.PermissionPrompt);
    }

    [Fact]
    public async Task OtherStartupErrorsBecomeAFailedStatus()
    {
        using var h = Harness.Make(calibrated: true);
        h.Source.StartError = new CameraError(CameraErrorKind.NoDevice);

        await h.SetTracking(true);

        Assert.Equal(new CaptureStatus.Failed(new CameraError(CameraErrorKind.NoDevice)), h.Controller.Status);
        Assert.False(h.Controller.TrackingEnabled);
        Assert.Null(h.Controller.PermissionPrompt);
        Assert.Equal(TrayIconState.Attention, h.Controller.TrayIcon);
    }

    [Fact]
    public async Task AnUnexpectedExceptionFromStartBecomesAConfigurationFailure()
    {
        using var h = Harness.Make(calibrated: true);
        h.Source.ThrowOnStart = new InvalidOperationException("boom");

        await h.SetTracking(true);

        var failed = Assert.IsType<CaptureStatus.Failed>(h.Controller.Status);
        Assert.Equal(CameraErrorKind.ConfigurationFailed, failed.Error.Kind);
        Assert.False(h.Controller.TrackingEnabled);
    }

    [Fact]
    public async Task GrantingAccessInSettingsClearsTheInstructions()
    {
        using var h = Harness.Make(authorization: CameraAuthorization.Denied, calibrated: true);
        await h.SetTracking(true);
        Assert.Equal(CameraPermissionPrompt.Denied, h.Controller.PermissionPrompt);
        Assert.Equal(0, h.Source.StartCount);

        h.Cameras.AuthorizationValue = CameraAuthorization.Authorized;
        h.Controller.RefreshEnvironment();
        h.Drain();

        Assert.Null(h.Controller.PermissionPrompt);
        // The switch was left on, blocked: granting access starts the camera without another click.
        Assert.Equal(1, h.Source.StartCount);
    }

    // MARK: Application lifecycle

    [Fact]
    public async Task ShutdownStopsTheCameraAndLeavesTheSavedChoice()
    {
        using var h = Harness.Make(autoRun: true, calibrated: true);
        await h.SetTracking(true);

        await h.Controller.ShutdownAsync();
        h.Drain();

        Assert.False(h.Controller.TrackingEnabled);
        Assert.Equal("true", h.Settings.Values[SettingsKeys.TrackingEnabled]); // Quitting is not the user's choice.
        Assert.True(h.Source.StopCount >= 1);
        Assert.False(h.Controller.IsCameraActive);
    }

    // MARK: State changes

    [Fact]
    public async Task StateChangedFiresOnlyWhenWhatTheUiShowsChanges()
    {
        using var h = Harness.Make(calibrated: true);
        await h.StartTracking();
        h.Feed(1);
        var count = h.StateChangedCount;

        for (var i = 2; i < 40; i++)
        {
            h.Feed(i / 15.0);
        }

        Assert.Equal(count, h.StateChangedCount); // Frames alone never raise it.
        h.Controller.SetFocusLock(true);
        Assert.Equal(count + 1, h.StateChangedCount);
    }
}
