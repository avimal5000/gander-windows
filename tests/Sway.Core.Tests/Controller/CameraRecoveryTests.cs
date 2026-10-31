using Sway.Core.Controller;
using Sway.Core.Platform;
using Sway.Core.Runtime;

namespace Sway.Core.Tests.Controller;

/// <summary>
/// Port of the Mac AppModelCameraRecoveryTests (Mac CONTRACT 6.5.5 items 4 to 8, SPEC D46 revised). Not ported: the three
/// tests that run while the Setup window or the Gate test owns the camera (those flows are out of scope; the controller has
/// the generic CalibrationRunning flag, covered in the Lifecycle tests), and the "FaceTime HD Camera" name is
/// "Integrated Camera" here.
/// </summary>
public class CameraRecoveryTests
{
    private static readonly CameraDevice BuiltIn = Fixtures.BuiltInCamera;
    private static readonly CameraDevice External = Fixtures.ExternalCamera;
    private static readonly CameraDevice OtherUsb = Fixtures.SecondExternalCamera;

    private static readonly CameraError NotReady = new(CameraErrorKind.DeviceUnavailable, "Camera is not ready");

    private static async Task<Harness> TrackingHarness()
    {
        var h = Harness.Make(calibrated: true);
        await h.StartTracking();
        h.Drain();
        return h;
    }

    private static string[] Ids(Harness h) => h.Source.Configurations.Select(c => c.CameraDeviceId ?? "(default)").ToArray();

    // MARK: Item 4: only a running camera switches

    [Fact]
    public async Task OnlyARunningCameraSwitches()
    {
        using var h = await TrackingHarness();
        h.Focus.Result = TrackingTests.FocusedRight();
        h.Engine.Script.Rule = (sample, _) => Fixtures.Decision(
            switchTo: Fixtures.RightId,
            phase: SwitchPhase.CoolingDown(sample.Timestamp + 0.75),
            classification: Fixtures.Classification(best: Fixtures.RightId));
        var recorder = h.Engine.LastRecorder!;

        CaptureStatus[] others =
        [
            new CaptureStatus.Interrupted("Camera disconnected"),
            new CaptureStatus.Suspended(SuspendReason.ScreenLocked),
            new CaptureStatus.Starting(),
            new CaptureStatus.Stopped(),
        ];
        foreach (var status in others)
        {
            h.ReportStatus(status);
            h.Feed(1.4);
            Assert.Empty(recorder.Samples); // Must not feed the decider.
        }

        Assert.Empty(h.Focus.FocusCalls);
        h.ReportRunning();
        h.Feed(1.5);
        Assert.Equal([Fixtures.RightId], h.Focus.FocusCalls); // Running again: the look counts.
    }

    // MARK: Item 5: fewer environment refreshes

    [Fact]
    public void ABurstOfCameraNotificationsBecomesOneRefreshHalfASecondAfterTheLast()
    {
        using var h = Harness.Make(calibrated: true);
        var reads = h.Cameras.DeviceListCalls;

        h.Cameras.NotifyOnly();

        h.Drain();
        h.Advance(0.3);
        h.Cameras.NotifyOnly();
        h.Drain();
        h.Advance(0.3);
        h.Cameras.NotifyOnly();
        h.Drain();
        Assert.Equal(0.5, TrackingController.EnvironmentRefreshDelay);
        h.Advance(0.49);
        Assert.Equal(reads, h.Cameras.DeviceListCalls); // Not read on each notification, nor 0.5 s after the first.
        h.Advance(0.01);
        Assert.Equal(reads + 1, h.Cameras.DeviceListCalls);
        h.Advance(10);
        Assert.Equal(reads + 1, h.Cameras.DeviceListCalls); // One refresh for the whole burst.
    }

    [Fact]
    public async Task NothingIsReadWhileTheCameraIsSuspended()
    {
        using var h = await TrackingHarness();
        h.ReportStatus(new CaptureStatus.Suspended(SuspendReason.ScreenLocked));
        var reads = h.Cameras.DeviceListCalls;

        h.Cameras.NotifyOnly();

        h.Drain();
        h.Cameras.NotifyOnly();
        h.Drain();

        Assert.False(h.Scheduler.HasPending); // Nothing is even scheduled.
        h.Advance(10);
        Assert.Equal(reads, h.Cameras.DeviceListCalls);
    }

    [Fact]
    public async Task ARefreshWaitingWhenTheCameraSuspendsIsSkipped()
    {
        using var h = await TrackingHarness();
        var reads = h.Cameras.DeviceListCalls;

        h.Cameras.NotifyOnly();
        h.Drain();
        h.ReportStatus(new CaptureStatus.Suspended(SuspendReason.SystemSleep));
        h.Advance(0.5);

        Assert.Equal(reads, h.Cameras.DeviceListCalls);
    }

    [Fact]
    public async Task LeavingSuspendedReadsTheCameraListOnce()
    {
        using var h = await TrackingHarness();
        h.ReportStatus(new CaptureStatus.Suspended(SuspendReason.ScreenLocked));
        h.Cameras.NotifyOnly();
        h.Drain();
        var reads = h.Cameras.DeviceListCalls;

        h.ReportStatus(new CaptureStatus.Suspended(SuspendReason.DisplaysAsleep));
        Assert.Equal(reads, h.Cameras.DeviceListCalls); // Still suspended.
        h.ReportStatus(new CaptureStatus.Starting());
        Assert.Equal(reads + 1, h.Cameras.DeviceListCalls);
        h.ReportStatus(new CaptureStatus.Running(BuiltIn));
        Assert.Equal(reads + 1, h.Cameras.DeviceListCalls); // Once, when the status leaves suspended.
    }

    [Fact]
    public void TheShellStillRefreshesAtOnce()
    {
        using var h = Harness.Make(calibrated: true);
        var reads = h.Cameras.DeviceListCalls;

        h.Controller.RefreshEnvironment();

        Assert.Equal(reads + 1, h.Cameras.DeviceListCalls);
    }

    // MARK: Item 6: the camera waits 2 s after a display change

    private static async Task<Harness> GraceHarness()
    {
        var h = Harness.Make(layout: Fixtures.ThreeDisplays, autoRun: true, calibrated: true);
        await h.SetTracking(true);
        return h;
    }

    private static void Layout(Harness h, DisplayLayout layout)
    {
        h.Displays.Change(layout);
        h.Drain();
    }

    [Fact]
    public async Task ADisplayChangeThatLeavesNoCalibrationStopsTheCameraTwoSecondsLater()
    {
        using var h = await GraceHarness();

        Layout(h, Fixtures.OneDisplay);

        Assert.Null(h.Controller.ActiveCalibration);
        Assert.True(h.Controller.IsCameraActive);
        Assert.Equal(0, h.Source.StopCount);
        Assert.Equal([TrackingController.CameraGraceDelay], h.Scheduler.PendingDelays);
        Assert.Equal(2, TrackingController.CameraGraceDelay);
        h.Advance(1.99);
        Assert.True(h.Controller.IsCameraActive); // Not yet.
        h.Advance(0.01);
        Assert.False(h.Controller.IsCameraActive);
        Assert.True(h.Source.StopCount >= 1);
        Assert.Equal(TrackingStatusKind.OneDisplay, h.Controller.TrackingStatus.Kind);
    }

    [Fact]
    public async Task AnUnknownDisplaySetGetsTheSameGrace()
    {
        using var h = await GraceHarness();

        Layout(h, Fixtures.FourDisplays);
        Assert.True(h.Controller.IsCameraActive);
        Assert.Equal(TrackingStatusKind.NeedsCalibration, h.Controller.TrackingStatus.Kind);

        h.Advance(2);
        Assert.False(h.Controller.IsCameraActive);
    }

    [Fact]
    public async Task ALayoutThatRecoversWithinTheGraceCancelsTheStop()
    {
        using var h = await GraceHarness();
        Layout(h, Fixtures.OneDisplay);
        h.Advance(1.5);

        Layout(h, Fixtures.ThreeDisplays);

        Assert.False(h.Scheduler.HasPending); // The stop is cancelled.
        h.Advance(10);
        Assert.True(h.Controller.IsCameraActive);
        Assert.Equal(0, h.Source.StopCount);
        Assert.Equal(1, h.Source.StartCount); // The camera never blinked.
        Assert.NotNull(h.Controller.ActiveCalibration);
    }

    [Fact]
    public async Task NoSwitchingHappensDuringTheGrace()
    {
        using var h = await GraceHarness();
        Layout(h, Fixtures.OneDisplay);
        var count = h.Engine.LastRecorder?.Samples.Count;

        for (var i = 1; i <= 10; i++)
        {
            h.Feed(i * 0.1);
        }

        Assert.True(h.Controller.IsCameraActive);
        Assert.Equal(count, h.Engine.LastRecorder?.Samples.Count); // Nothing is calibrated, so the decider sees nothing.
        Assert.Empty(h.Focus.FocusCalls);
    }

    [Fact]
    public async Task ASecondDisplayChangeDoesNotExtendTheGrace()
    {
        using var h = await GraceHarness();
        Layout(h, Fixtures.OneDisplay);
        h.Advance(1);
        Layout(h, Fixtures.FourDisplays);
        h.Advance(1);

        Assert.False(h.Controller.IsCameraActive); // Two seconds after the first change.
    }

    [Fact]
    public async Task TurningTrackingOffDuringTheGraceStopsTheCameraAtOnce()
    {
        using var h = await GraceHarness();
        Layout(h, Fixtures.OneDisplay);
        Assert.True(h.Controller.IsCameraActive);

        await h.SetTracking(false);

        Assert.False(h.Controller.IsCameraActive);
        Assert.False(h.Scheduler.HasPending);
    }

    [Fact]
    public async Task QuittingDuringTheGraceStopsTheCameraAtOnce()
    {
        using var h = await GraceHarness();
        Layout(h, Fixtures.OneDisplay);

        await h.Controller.ShutdownAsync();
        h.Drain();

        Assert.False(h.Controller.IsCameraActive);
        Assert.True(h.Source.StopCount >= 1);
        Assert.False(h.Scheduler.HasPending);
    }

    [Fact]
    public async Task ACalibrationRemovedByTheUserStopsTheCameraAtOnce()
    {
        using var h = Harness.Make(autoRun: true, calibrated: true);
        await h.SetTracking(true);
        var key = h.Controller.ActiveCalibration!.Saved.Key;

        h.Controller.RemoveFromLibrary(key);
        h.Drain();

        Assert.False(h.Controller.IsCameraActive); // Only a display change gets the grace.
        Assert.DoesNotContain(TrackingController.CameraGraceDelay, h.Scheduler.ScheduledDelays);
    }

    [Fact]
    public async Task ChoosingACameraWithoutACalibrationStopsTheCameraAtOnce()
    {
        using var h = Harness.Make(autoRun: true, calibrated: true);
        await h.SetTracking(true);

        await h.Controller.SelectCameraAsync(External.Id);
        h.Drain();

        Assert.False(h.Controller.IsCameraActive);
        Assert.DoesNotContain(TrackingController.CameraGraceDelay, h.Scheduler.ScheduledDelays);
    }

    [Fact]
    public void ADisplayChangeWhileNothingRunsSchedulesNothing()
    {
        using var h = Harness.Make(layout: Fixtures.ThreeDisplays, calibrated: true);

        Layout(h, Fixtures.OneDisplay);

        Assert.False(h.Scheduler.HasPending);
    }

    // MARK: Item 7: the camera fallback

    private static async Task<Harness> FallbackHarness(CameraDevice[]? cameras = null, CameraDevice[]? calibrated = null)
    {
        cameras ??= [BuiltIn, External];
        calibrated ??= [BuiltIn, External];
        var h = Harness.Make(
            autoRun: true, savedCameraId: External.Id, calibrated: true, calibrationCameras: calibrated.Select(c => c.Name).ToArray());
        h.Cameras.Devices = cameras.ToList();
        h.Controller.RefreshEnvironment();
        await h.SetTracking(true);
        return h;
    }

    // The chosen camera is unplugged: capture is interrupted, and the controller learns of it half a second later.
    private static void UnplugExternal(Harness h, CameraDevice[]? remaining = null)
    {
        h.Cameras.Devices = (remaining ?? [BuiltIn]).ToList();
        h.ReportStatus(new CaptureStatus.Interrupted("Camera disconnected"));
        h.Cameras.NotifyOnly();
        h.Drain();
        h.Advance(TrackingController.EnvironmentRefreshDelay);
    }

    private static void ReplugExternal(Harness h, CameraDevice[]? cameras = null)
    {
        h.Cameras.Devices = (cameras ?? [BuiltIn, External]).ToList();
        h.Cameras.NotifyOnly();
        h.Drain();
        h.Advance(TrackingController.EnvironmentRefreshDelay);
    }

    [Fact]
    public async Task TheChosenCameraUnpluggedForFiveSecondsFallsBackToACalibratedCamera()
    {
        using var h = await FallbackHarness();

        UnplugExternal(h);
        Assert.Equal(TrackingStatus.CameraDisconnected(External.Name), h.Controller.TrackingStatus);
        Assert.Equal("Logitech C922 disconnected", h.Controller.StatusLine);
        Assert.Equal(External.Name, h.Controller.ActiveCalibration!.Saved.CameraName); // Waiting keeps its own calibration.
        Assert.True(h.Controller.IsCameraActive);
        Assert.Equal(5, TrackingController.CameraFallbackDelay);

        h.Advance(4.99);
        Assert.Equal(1, h.Source.StartCount); // Not yet.
        h.Advance(0.01);

        Assert.Equal([External.Id, BuiltIn.Id], Ids(h));
        Assert.Equal(BuiltIn, h.Controller.FallbackCamera);
        Assert.Equal(new CaptureStatus.Running(BuiltIn), h.Controller.Status);
        Assert.Equal(BuiltIn.Name, h.Controller.ActiveCalibration!.Saved.CameraName);
        Assert.Equal(TrackingStatus.NoFace, h.Controller.TrackingStatus);
    }

    [Fact]
    public async Task AFallbackNeverChangesTheSavedChoice()
    {
        using var h = await FallbackHarness();
        UnplugExternal(h);
        h.Advance(5);

        Assert.NotNull(h.Controller.FallbackCamera);
        Assert.Equal(External.Id, h.Controller.SelectedCameraId);
        Assert.Equal(External.Id, h.Settings.Values[SettingsKeys.CameraDeviceId]);
        using var relaunched = h.Relaunch();
        Assert.Equal(External.Id, relaunched.Controller.SelectedCameraId);
    }

    [Fact]
    public async Task TheChosenCameraComingBackRestartsOnIt()
    {
        using var h = await FallbackHarness();
        UnplugExternal(h);
        h.Advance(5);
        Assert.Equal(new CaptureStatus.Running(BuiltIn), h.Controller.Status);

        ReplugExternal(h);

        Assert.Null(h.Controller.FallbackCamera);
        Assert.Equal([External.Id, BuiltIn.Id, External.Id], Ids(h));
        Assert.Equal(new CaptureStatus.Running(External), h.Controller.Status);
        Assert.Equal(External.Name, h.Controller.ActiveCalibration!.Saved.CameraName);
        Assert.Equal(External.Id, h.Controller.SelectedCameraId);
    }

    [Fact]
    public async Task TheChosenCameraComingBackBeforeTheFallbackJustKeepsGoing()
    {
        using var h = await FallbackHarness();
        UnplugExternal(h);
        h.Advance(3);

        // The pose source restarts the same device by itself when it returns.
        ReplugExternal(h);
        h.ReportStatus(new CaptureStatus.Starting());
        h.ReportStatus(new CaptureStatus.Running(External));
        h.Advance(10);

        Assert.Equal(1, h.Source.StartCount); // Sway never restarted anything.
        Assert.Null(h.Controller.FallbackCamera);
        Assert.Equal(TrackingStatus.NoFace, h.Controller.TrackingStatus);
    }

    [Fact]
    public async Task ACalibratedCameraTakesOverWhileTheWaitBeforeItShowsTheMissingCamera()
    {
        using var h = await FallbackHarness(calibrated: [External, BuiltIn]);
        UnplugExternal(h);

        h.Advance(4.99);
        Assert.Equal("Logitech C922 disconnected", h.Controller.StatusLine); // Waiting for the five seconds.
        Assert.True(h.Controller.TrackingEnabled);
        Assert.Equal(0, h.Source.StopCount);
        h.Advance(0.01);

        Assert.True(h.Controller.TrackingEnabled); // A calibrated camera exists: nothing turns off.
        Assert.Equal([External.Id, BuiltIn.Id], Ids(h));
    }

    [Fact]
    public async Task AStandInThatIsNotTheBuiltInCameraKeepsTrackingRunningAfterTheRestart()
    {
        using var h = await FallbackHarness(cameras: [OtherUsb, BuiltIn, External], calibrated: [External, OtherUsb]);

        UnplugExternal(h, remaining: [OtherUsb, BuiltIn]);
        h.Advance(5);

        Assert.Equal(OtherUsb, h.Controller.FallbackCamera); // The built-in camera has no calibration.
        Assert.Equal([External.Id, OtherUsb.Id], Ids(h));
        Assert.Equal(new CaptureStatus.Running(OtherUsb), h.Controller.Status);
        Assert.True(h.Controller.IsCameraActive);
        Assert.Equal(OtherUsb.Name, h.Controller.ActiveCalibration!.Saved.CameraName);
    }

    [Fact]
    public async Task ACameraThatIsNotConnectedIsNeverAFallback()
    {
        using var h = await FallbackHarness(calibrated: [BuiltIn, External]);

        UnplugExternal(h, remaining: [OtherUsb]);
        h.Advance(30);

        Assert.Null(h.Controller.FallbackCamera); // The built-in camera has a calibration but is not connected.
        Assert.Equal(1, h.Source.StartCount);
        Assert.False(h.Controller.TrackingEnabled); // Nothing usable is left, so Tracking is off.
    }

    [Fact]
    public async Task ACameraStillConnectedButBusyNeverFallsBack()
    {
        using var h = await FallbackHarness();

        h.ReportStatus(new CaptureStatus.Interrupted("Camera is in use by another app"));
        h.Advance(30);

        Assert.Equal("Camera busy", h.Controller.StatusLine);
        Assert.Equal(1, h.Source.StartCount);
        Assert.Null(h.Controller.FallbackCamera);
    }

    [Fact]
    public async Task ARecoveryBeforeTheFiveSecondsStartsTheWaitAgain()
    {
        using var h = await FallbackHarness();
        UnplugExternal(h);
        h.Advance(3);

        h.ReportStatus(new CaptureStatus.Running(External));
        Assert.False(h.Scheduler.HasPending); // Frames came back: the wait is over.

        h.ReportStatus(new CaptureStatus.Interrupted("Camera disconnected"));
        h.Advance(3);
        Assert.Equal(1, h.Source.StartCount); // Three seconds into the new wait.
        h.Advance(2);
        Assert.Equal(2, h.Source.StartCount);
    }

    [Fact]
    public async Task ADefaultChoiceNeverFallsBack()
    {
        using var h = Harness.Make(autoRun: true, calibrated: true);
        await h.SetTracking(true);

        h.Cameras.Devices = [External];
        h.ReportStatus(new CaptureStatus.Interrupted("Camera disconnected"));
        h.Cameras.NotifyOnly();
        h.Drain();
        h.Advance(30);

        Assert.Equal(1, h.Source.StartCount);
        Assert.Null(h.Controller.FallbackCamera);
    }

    [Fact]
    public async Task ACameraCaptureOpenedOnItsOwnIsNotTheChosenOne()
    {
        // The chosen camera was never connected in this run: capture opened the built-in one instead.
        using var h = Harness.Make(savedCameraId: External.Id, calibrated: true, calibrationCameras: [BuiltIn.Name, External.Name]);
        h.Cameras.Devices = [BuiltIn];
        h.Controller.RefreshEnvironment();
        await h.SetTracking(true);
        h.ReportRunning();

        h.ReportStatus(new CaptureStatus.Interrupted("Camera disconnected"));
        h.Advance(30);

        Assert.Equal("Interrupted: Camera disconnected", h.Controller.StatusLine);
        Assert.Equal(1, h.Source.StartCount);
    }

    [Fact]
    public async Task TheChosenCameraComingBackWhileSuspendedWaitsForTheWake()
    {
        using var h = await FallbackHarness();
        UnplugExternal(h);
        h.Advance(5);
        Assert.Equal(new CaptureStatus.Running(BuiltIn), h.Controller.Status);
        h.RaiseSession(SessionEvent.ScreenLocked);
        var starts = h.Source.StartCount;

        h.Cameras.Devices = [BuiltIn, External];
        h.Controller.RefreshEnvironment();
        h.Drain();

        Assert.Equal(starts, h.Source.StartCount); // Nothing restarts while the camera is suspended.
        Assert.Equal(BuiltIn, h.Controller.FallbackCamera);

        h.RaiseSession(SessionEvent.ScreenUnlocked);
        h.Advance(2);

        Assert.Null(h.Controller.FallbackCamera);
        Assert.Equal(External.Id, Ids(h).Last());
    }

    [Fact]
    public async Task TheBuiltInCameraIsPreferredAndOtherwiseTheFirstCalibratedOne()
    {
        using var h = await FallbackHarness(cameras: [OtherUsb, BuiltIn, External], calibrated: [BuiltIn, External, OtherUsb]);
        h.Cameras.Devices = [OtherUsb, BuiltIn];
        h.Controller.RefreshEnvironment();
        h.Drain();

        UnplugExternal(h, remaining: [OtherUsb, BuiltIn]);
        h.Advance(5);
        Assert.Equal(BuiltIn, h.Controller.FallbackCamera);

        using var second = await FallbackHarness(cameras: [OtherUsb, BuiltIn, External], calibrated: [External, OtherUsb]);
        UnplugExternal(second, remaining: [OtherUsb, BuiltIn]);
        second.Advance(5);
        Assert.Equal(OtherUsb, second.Controller.FallbackCamera);
    }

    // MARK: No usable fallback turns Tracking off (SPEC D46, revised)

    private static async Task<Harness> LostCameraHarness()
    {
        var h = await FallbackHarness(calibrated: [External]);
        UnplugExternal(h);
        h.Advance(TrackingController.CameraFallbackDelay);
        return h;
    }

    [Fact]
    public async Task WithNoCalibratedCameraTheFiveSecondsTurnTrackingOffAndStopTheCamera()
    {
        using var h = await FallbackHarness(calibrated: [External]);
        UnplugExternal(h);

        h.Advance(4.99);
        Assert.True(h.Controller.TrackingEnabled); // Not yet.
        Assert.True(h.Controller.IsCameraActive);
        Assert.Equal("Logitech C922 disconnected", h.Controller.StatusLine);
        h.Advance(0.01);

        Assert.False(h.Controller.TrackingEnabled);
        Assert.False(h.Controller.IsCameraActive); // The camera light goes off.
        Assert.True(h.Source.StopCount >= 1);
        Assert.IsType<CaptureStatus.Stopped>(h.Controller.Status);
        Assert.False(h.Scheduler.HasPending); // No timer is left behind.
        Assert.Null(h.Controller.FallbackCamera);
        Assert.Equal(1, h.Source.StartCount);
    }

    [Fact]
    public async Task TurningTrackingOffForALostCameraNeverWritesTheSavedChoice()
    {
        using var h = await LostCameraHarness();

        Assert.False(h.Controller.TrackingEnabled);
        Assert.Equal("true", h.Settings.Values[SettingsKeys.TrackingEnabled]); // The user's choice, still on (D23).
        using var relaunched = h.Relaunch();
        Assert.True(relaunched.Controller.TrackingEnabled);
        Assert.Equal(External.Id, h.Controller.SelectedCameraId);
        Assert.Equal(External.Id, h.Settings.Values[SettingsKeys.CameraDeviceId]);
    }

    [Fact]
    public async Task WhileTheCameraIsGoneTheStatusNamesItInsteadOfPaused()
    {
        using var h = await LostCameraHarness();

        Assert.Equal(TrackingStatus.CameraDisconnected(External.Name), h.Controller.TrackingStatus);
        Assert.Equal("Logitech C922 disconnected", h.Controller.StatusLine);
        Assert.Equal(TrayIconState.Attention, h.Controller.TrayIcon); // The same "needs you" icon as before.
    }

    [Fact]
    public async Task WhenTheCameraComesBackTheStatusIsPausedAndTrackingStaysOff()
    {
        using var h = await LostCameraHarness();
        var starts = h.Source.StartCount;

        ReplugExternal(h);
        h.Advance(30);

        Assert.Equal(TrackingStatus.Paused, h.Controller.TrackingStatus);
        Assert.Equal("Paused", h.Controller.StatusLine);
        Assert.False(h.Controller.TrackingEnabled); // Tracking does not come back by itself.
        Assert.Equal(starts, h.Source.StartCount);
        Assert.False(h.Controller.IsCameraActive);
        Assert.Equal(TrayIconState.Paused, h.Controller.TrayIcon);
    }

    [Fact]
    public async Task TheUserCanTurnTrackingOnAfterTheCameraReturnsAndItRunsOnTheChosenCamera()
    {
        using var h = await LostCameraHarness();
        ReplugExternal(h);

        await h.SetTracking(true);

        Assert.True(h.Controller.TrackingEnabled);
        Assert.Equal(External.Id, Ids(h).Last());
        Assert.Equal(new CaptureStatus.Running(External), h.Controller.Status);
        Assert.Equal(External.Name, h.Controller.ActiveCalibration!.Saved.CameraName);
        Assert.Equal("true", h.Settings.Values[SettingsKeys.TrackingEnabled]);
    }

    [Fact]
    public async Task TurningTrackingOnWhileTheCameraIsStillGoneAddsNoLoop()
    {
        using var h = await LostCameraHarness();
        var starts = h.Source.StartCount;

        // Nothing is calibrated for the camera capture would open now: Tracking waits for a calibration, as before.
        await h.SetTracking(true);
        h.Advance(60);

        Assert.True(h.Controller.TrackingEnabled);
        Assert.Equal(TrackingStatusKind.NeedsCalibration, h.Controller.TrackingStatus.Kind);
        Assert.Equal(starts, h.Source.StartCount);
        Assert.False(h.Scheduler.HasPending);
    }

    [Fact]
    public async Task TurningTrackingOnWhileTheCameraIsStillGoneAndNotReadyUsesTheExistingFailurePath()
    {
        using var h = await LostCameraHarness();
        h.Controller.SaveToLibrary(Fixtures.Profile(Fixtures.TwoDisplays, BuiltIn.Name));
        h.Source.FailNextStarts(new CameraError(CameraErrorKind.NoDevice));

        await h.SetTracking(true);
        h.Advance(60);

        Assert.False(h.Controller.TrackingEnabled); // Turned off again, as for any camera that cannot start.
        Assert.Equal(TrackingStatusKind.CameraFailed, h.Controller.TrackingStatus.Kind);
        Assert.Equal(2, h.Source.StartCount); // One try, no loop.
        Assert.False(h.Scheduler.HasPending);
    }

    [Fact]
    public async Task PickingAnotherCameraEndsTheLostCameraStatus()
    {
        using var h = await LostCameraHarness();

        await h.Controller.SelectCameraAsync(BuiltIn.Id);
        h.Drain();

        Assert.NotEqual(TrackingStatus.CameraDisconnected(External.Name), h.Controller.TrackingStatus);
        Assert.Null(h.Controller.LostCameraId);
    }

    [Fact]
    public async Task AChosenCameraThatIsStillConnectedNeverTurnsTrackingOff()
    {
        using var h = await FallbackHarness(calibrated: [External]);

        h.ReportStatus(new CaptureStatus.Interrupted("Camera is in use by another app"));
        h.Advance(60);

        Assert.True(h.Controller.TrackingEnabled);
        Assert.Equal("Camera busy", h.Controller.StatusLine);
    }

    [Fact]
    public async Task AFailedRetryWithNothingLeftToUseTurnsTrackingOffAndClearsTheRetry()
    {
        using var h = await FallbackHarness(calibrated: [External, BuiltIn]);
        h.Source.FailNextStarts(new CameraError(CameraErrorKind.NoDevice));
        UnplugExternal(h);
        h.Advance(5);
        Assert.True(h.Controller.TrackingEnabled); // The first failure only waits for the next try.
        Assert.True(h.Scheduler.HasPending); // A retry is waiting.

        h.Cameras.Devices = [];
        h.Cameras.NotifyOnly();
        h.Drain();
        h.Advance(TrackingController.EnvironmentRefreshDelay);
        h.Advance(TrackingController.CameraFallbackDelay);

        Assert.False(h.Controller.TrackingEnabled);
        Assert.Equal(TrackingStatus.CameraDisconnected(External.Name), h.Controller.TrackingStatus);
        Assert.False(h.Scheduler.HasPending); // The retry is cancelled with it.
    }

    [Fact]
    public async Task NothingTurnsOffWhileACalibrationRunsAndItDoesAfterItEnds()
    {
        using var h = await FallbackHarness(calibrated: [External]);
        await h.Controller.SetCalibrationRunningAsync(true);
        h.Drain();

        UnplugExternal(h);
        h.Advance(30);
        Assert.True(h.Controller.TrackingEnabled);
        Assert.True(h.Controller.IsCameraActive);
        Assert.Null(h.Controller.LostCameraId);

        await h.Controller.SetCalibrationRunningAsync(false);
        h.Drain();
        h.Advance(TrackingController.CameraFallbackDelay);
        Assert.False(h.Controller.TrackingEnabled); // The wait had to run again after the calibration.
        Assert.False(h.Controller.IsCameraActive);
    }

    [Fact]
    public async Task NeverSwitchesCamerasWhileACalibrationRunsAndTheWaitThatRanOutEndsAfterIt()
    {
        using var h = await FallbackHarness();
        await h.Controller.SetCalibrationRunningAsync(true);
        h.Drain();
        var starts = h.Source.StartCount;

        UnplugExternal(h);
        h.Advance(30);
        Assert.Equal(starts, h.Source.StartCount);
        Assert.Null(h.Controller.FallbackCamera);

        await h.Controller.SetCalibrationRunningAsync(false);
        h.Drain();
        h.Advance(TrackingController.CameraFallbackDelay);
        Assert.Equal(BuiltIn, h.Controller.FallbackCamera);
        Assert.Equal(BuiltIn.Id, Ids(h).Last());
    }

    [Fact]
    public async Task TheChosenCameraComingBackWaitsForACalibrationToEnd()
    {
        using var h = await FallbackHarness();
        UnplugExternal(h);
        h.Advance(5);
        Assert.Equal(BuiltIn, h.Controller.FallbackCamera);
        await h.Controller.SetCalibrationRunningAsync(true);
        h.Drain();
        var starts = h.Source.StartCount;

        ReplugExternal(h);
        Assert.Equal(starts, h.Source.StartCount); // A calibration is running: the camera stays as it is.
        Assert.Equal(BuiltIn, h.Controller.FallbackCamera);

        await h.Controller.SetCalibrationRunningAsync(false);
        h.Drain();
        h.Advance(TrackingController.EnvironmentRefreshDelay);
        Assert.Null(h.Controller.FallbackCamera);
        Assert.Equal(External.Id, Ids(h).Last());
    }

    [Fact]
    public async Task NothingTurnsOffWhileTheCameraIsSuspended()
    {
        using var h = await FallbackHarness(calibrated: [External]);
        UnplugExternal(h);
        h.Advance(3);

        h.ReportStatus(new CaptureStatus.Suspended(SuspendReason.ScreenLocked));
        h.Advance(60);
        Assert.True(h.Controller.TrackingEnabled);
        Assert.True(h.Controller.IsCameraActive);
        Assert.Equal(0, h.Source.StopCount);

        // After the wake the camera is still missing: the five seconds start again.
        h.ReportStatus(new CaptureStatus.Interrupted("Camera disconnected"));
        h.Advance(4.99);
        Assert.True(h.Controller.TrackingEnabled);
        h.Advance(0.01);
        Assert.False(h.Controller.TrackingEnabled);
    }

    private static async Task<Harness> StandInHarness()
    {
        var h = await FallbackHarness();
        UnplugExternal(h);
        h.Advance(5);
        return h;
    }

    [Fact]
    public async Task AStandInThatIsAlsoDisconnectedWithNothingElseUsableTurnsTrackingOff()
    {
        using var h = await StandInHarness();
        Assert.Equal(BuiltIn, h.Controller.FallbackCamera);

        h.Cameras.Devices = [];
        h.ReportStatus(new CaptureStatus.Interrupted("Camera disconnected"));
        h.Cameras.NotifyOnly();
        h.Drain();
        h.Advance(TrackingController.EnvironmentRefreshDelay);
        h.Advance(4.99);
        Assert.True(h.Controller.TrackingEnabled); // Not yet.
        h.Advance(0.01);

        Assert.False(h.Controller.TrackingEnabled);
        Assert.False(h.Controller.IsCameraActive);
        Assert.Null(h.Controller.FallbackCamera);
        Assert.Equal(TrackingStatus.CameraDisconnected(External.Name), h.Controller.TrackingStatus); // The camera the user chose.
        Assert.Equal("true", h.Settings.Values[SettingsKeys.TrackingEnabled]);
        Assert.False(h.Scheduler.HasPending);
    }

    [Fact]
    public async Task AStandInThatIsAlsoDisconnectedMovesToAnotherCalibratedCameraIfThereIsOne()
    {
        using var h = await FallbackHarness(cameras: [BuiltIn, External, OtherUsb], calibrated: [External, BuiltIn, OtherUsb]);
        UnplugExternal(h, remaining: [BuiltIn, OtherUsb]);
        h.Advance(5);
        Assert.Equal(BuiltIn, h.Controller.FallbackCamera);

        h.Cameras.Devices = [OtherUsb];
        h.ReportStatus(new CaptureStatus.Interrupted("Camera disconnected"));
        h.Cameras.NotifyOnly();
        h.Drain();
        h.Advance(TrackingController.EnvironmentRefreshDelay);
        h.Advance(5);

        Assert.True(h.Controller.TrackingEnabled);
        Assert.Equal(OtherUsb, h.Controller.FallbackCamera);
    }

    // MARK: A failed restart during a fallback never turns Tracking off

    [Fact]
    public async Task AFailedSwitchBackKeepsTrackingOnAndPutsTheStandInBack()
    {
        using var h = await StandInHarness();
        h.Source.FailNextStarts(NotReady);

        ReplugExternal(h);

        Assert.True(h.Controller.TrackingEnabled); // A camera just plugged in may not be ready to open.
        Assert.Equal([External.Id, BuiltIn.Id, External.Id, BuiltIn.Id], Ids(h));
        Assert.Equal(BuiltIn, h.Controller.FallbackCamera);
        Assert.Equal(new CaptureStatus.Running(BuiltIn), h.Controller.Status);
        Assert.NotEqual(TrackingStatusKind.CameraFailed, h.Controller.TrackingStatus.Kind);
        Assert.Equal(External.Id, h.Controller.SelectedCameraId);
        Assert.Equal("true", h.Settings.Values[SettingsKeys.TrackingEnabled]);
    }

    [Fact]
    public async Task AFailedSwitchBackIsNotRetriedInALoopAndTriesAgainLater()
    {
        using var h = await StandInHarness();
        h.Source.FailNextStarts(new CameraError(CameraErrorKind.NoDevice));
        ReplugExternal(h);
        Assert.Equal(4, h.Source.StartCount);

        h.Controller.RefreshEnvironment(); // The stand-in's own changes must not trigger another switch back at once.
        h.Drain();
        Assert.Equal(4, h.Source.StartCount);

        h.Advance(TrackingController.CameraFallbackDelay);
        Assert.Equal(5, h.Source.StartCount);
        Assert.Equal(External.Id, Ids(h).Last());
        Assert.Null(h.Controller.FallbackCamera);
        Assert.Equal(new CaptureStatus.Running(External), h.Controller.Status);
        Assert.True(h.Controller.TrackingEnabled);
    }

    [Fact]
    public async Task ADeviceChangeTriesAFailedSwitchBackAgain()
    {
        using var h = await StandInHarness();
        h.Source.FailNextStarts(NotReady);
        ReplugExternal(h);
        Assert.Equal(4, h.Source.StartCount);

        h.Cameras.Devices = [BuiltIn, External, OtherUsb];
        h.Cameras.NotifyOnly();
        h.Drain();
        h.Advance(TrackingController.EnvironmentRefreshDelay);

        Assert.Equal(External.Id, Ids(h).Last());
        Assert.Null(h.Controller.FallbackCamera);
    }

    [Fact]
    public async Task AFailedStandInStartKeepsTrackingOnAndTheNextDeviceChangeTriesAgain()
    {
        using var h = await FallbackHarness();
        h.Source.FailNextStarts(new CameraError(CameraErrorKind.NoDevice));
        UnplugExternal(h);
        h.Advance(5);

        Assert.True(h.Controller.TrackingEnabled);
        Assert.Equal("true", h.Settings.Values[SettingsKeys.TrackingEnabled]);
        Assert.False(h.Controller.IsCameraActive);
        Assert.Null(h.Controller.FallbackCamera);
        Assert.Equal(2, h.Source.StartCount);

        h.Cameras.NotifyOnly();

        h.Drain();
        h.Advance(TrackingController.EnvironmentRefreshDelay);

        Assert.Equal([External.Id, BuiltIn.Id, BuiltIn.Id], Ids(h));
        Assert.Equal(BuiltIn, h.Controller.FallbackCamera);
        Assert.Equal(new CaptureStatus.Running(BuiltIn), h.Controller.Status);
        Assert.True(h.Controller.TrackingEnabled);
    }

    [Fact]
    public async Task AFailedStandInStartIsAlsoRetriedByTheTimer()
    {
        using var h = await FallbackHarness();
        h.Source.FailNextStarts(NotReady);
        UnplugExternal(h);
        h.Advance(5);
        Assert.False(h.Controller.IsCameraActive);

        h.Advance(TrackingController.CameraFallbackDelay);

        Assert.Equal(BuiltIn, h.Controller.FallbackCamera);
        Assert.True(h.Controller.IsCameraActive);
        Assert.True(h.Controller.TrackingEnabled);
    }

    [Fact]
    public async Task ASwitchBackThatFailsAfterTheStandInAlsoFailedStartsTheChosenCameraWhenItIsReady()
    {
        using var h = await StandInHarness();
        h.Source.FailNextStarts(NotReady, new CameraError(CameraErrorKind.DeviceUnavailable, "Still busy"));

        ReplugExternal(h);
        Assert.True(h.Controller.TrackingEnabled);
        Assert.False(h.Controller.IsCameraActive);

        h.Advance(TrackingController.CameraFallbackDelay);

        Assert.Equal(External.Id, Ids(h).Last());
        Assert.True(h.Controller.IsCameraActive);
        Assert.True(h.Controller.TrackingEnabled);
    }

    [Fact]
    public async Task AnotherErrorOnTheStandInStartStillTurnsTrackingOff()
    {
        using var h = await FallbackHarness();
        h.Source.FailNextStarts(new CameraError(CameraErrorKind.ConfigurationFailed, "No format"));

        UnplugExternal(h);
        h.Advance(5);

        Assert.False(h.Controller.TrackingEnabled);
        Assert.Equal(TrackingStatusKind.CameraFailed, h.Controller.TrackingStatus.Kind);
    }

    [Fact]
    public async Task AnotherErrorOnTheSwitchBackStillTurnsTrackingOff()
    {
        using var h = await StandInHarness();
        h.Source.FailNextStarts(new CameraError(CameraErrorKind.RuntimeFailure, "Sensor died"));

        ReplugExternal(h);

        Assert.False(h.Controller.TrackingEnabled);
        Assert.Equal(TrackingStatusKind.CameraFailed, h.Controller.TrackingStatus.Kind);
        Assert.Null(h.Controller.FallbackCamera);
    }

    [Fact]
    public async Task ANotReadyCameraWhoseStartTheUserAskedForStillTurnsTrackingOff()
    {
        // Only the restarts the fallback itself begins are forgiven.
        using var h = await StandInHarness();
        h.Source.FailNextStarts(NotReady);

        await h.Controller.SelectCameraAsync(BuiltIn.Id);
        h.Drain();

        Assert.False(h.Controller.TrackingEnabled);
        Assert.Equal(TrackingStatusKind.CameraFailed, h.Controller.TrackingStatus.Kind);
    }

    [Fact]
    public async Task PausingWhileAFailedFallbackWaitsForItsRetryEndsTheWait()
    {
        using var h = await FallbackHarness();
        h.Source.FailNextStarts(new CameraError(CameraErrorKind.NoDevice));
        UnplugExternal(h);
        h.Advance(5);
        var starts = h.Source.StartCount;

        await h.SetTracking(false);
        h.Advance(30);

        Assert.Equal(starts, h.Source.StartCount);
        Assert.False(h.Controller.IsCameraActive);
    }

    [Fact]
    public async Task ChoosingACameraEndsTheFallback()
    {
        using var h = await FallbackHarness();
        UnplugExternal(h);
        h.Advance(5);
        Assert.NotNull(h.Controller.FallbackCamera);

        await h.Controller.SelectCameraAsync(BuiltIn.Id);
        h.Drain();

        Assert.Null(h.Controller.FallbackCamera);
        Assert.Equal(BuiltIn.Id, h.Controller.SelectedCameraId);
        Assert.Equal(BuiltIn.Id, Ids(h).Last());
    }

    [Fact]
    public async Task PausingDuringAFallbackForgetsItAndTheNextStartUsesTheSavedChoice()
    {
        using var h = await FallbackHarness();
        UnplugExternal(h);
        h.Advance(5);

        await h.SetTracking(false);
        Assert.Null(h.Controller.FallbackCamera);
        Assert.False(h.Controller.IsCameraActive);

        await h.SetTracking(true);
        Assert.Equal(External.Id, Ids(h).Last());
    }

    [Fact]
    public async Task QuittingForgetsTheWait()
    {
        using var h = await FallbackHarness();
        UnplugExternal(h);
        Assert.True(h.Scheduler.HasPending);

        await h.Controller.ShutdownAsync();
        h.Drain();

        Assert.False(h.Scheduler.HasPending);
    }

    // MARK: Item 8: a steadier "No face"

    [Fact]
    public async Task NoFaceShowsHalfASecondAfterTheLastFaceAndAFaceShowsAtOnce()
    {
        using var h = await TrackingHarness();
        h.Feed(1);
        Assert.Equal(TrackingStatus.Tracking, h.Controller.TrackingStatus);

        h.Feed(1.2, face: false);
        h.Feed(1.49, face: false);
        Assert.Equal(TrackingStatus.Tracking, h.Controller.TrackingStatus);
        Assert.True(h.Controller.HasFace);

        h.Feed(1.5, face: false);
        Assert.Equal(TrackingStatus.NoFace, h.Controller.TrackingStatus);
        Assert.Equal("No face · Integrated Camera", h.Controller.StatusLine);

        h.Feed(1.57);
        Assert.Equal(TrackingStatus.Tracking, h.Controller.TrackingStatus);
    }

    [Fact]
    public async Task TheDeciderSeesTheRawNoFaceSampleWhileOnlyTheStatusIsSteadied()
    {
        using var h = await TrackingHarness();
        h.Feed(1);
        var recorder = h.Engine.LastRecorder!;

        h.Feed(1.2, face: false);

        Assert.True(h.Controller.HasFace); // The status still shows the face.
        Assert.Equal(TrackingStatus.Tracking, h.Controller.TrackingStatus);
        Assert.Equal(2, recorder.Samples.Count);
        Assert.Null(recorder.Samples.Last().Pose); // The decider got the sample as it was.
    }

    [Fact]
    public async Task AGlitchOfOneFrameNeverShowsNoFace()
    {
        using var h = await TrackingHarness();

        for (var index = 0; index < 60; index++)
        {
            h.Feed(index / 15.0, face: index % 10 != 5);
            Assert.Equal(TrackingStatus.Tracking, h.Controller.TrackingStatus);
        }
    }

    [Fact]
    public async Task ARestartedCameraShowsNoFaceUntilTheFirstFaceAndThenAtOnce()
    {
        using var h = await TrackingHarness();
        h.Feed(1);

        h.ReportStatus(new CaptureStatus.Interrupted("Camera disconnected"));
        h.ReportRunning();
        h.Feed(1.1, face: false);
        Assert.False(h.Controller.HasFace); // Nothing to wait for after a restart.

        h.Feed(1.2);
        Assert.True(h.Controller.HasFace);
    }

    // MARK: Windows-specific: a vanished camera is reported as Failed(DeviceUnavailable)

    [Fact]
    public async Task AVanishedCameraReportedAsAFailureWaitsAndRetriesLikeAnInterruption()
    {
        using var h = await FallbackHarness(calibrated: [External]);

        h.Cameras.Devices = [BuiltIn];
        h.Source.FailNextStarts(Enumerable.Repeat(NotReady, 20).ToArray()); // The camera stays gone for every retry.
        h.ReportStatus(new CaptureStatus.Failed(new CameraError(CameraErrorKind.DeviceUnavailable, "Camera is not ready")));
        h.Cameras.NotifyOnly();
        h.Drain();
        h.Advance(TrackingController.EnvironmentRefreshDelay);

        Assert.True(h.Controller.TrackingEnabled); // Not a hard failure: the camera was running.
        Assert.IsType<CaptureStatus.Interrupted>(h.Controller.Status);
        Assert.Equal("Logitech C922 disconnected", h.Controller.StatusLine);

        h.Advance(5);

        Assert.False(h.Controller.TrackingEnabled);
        Assert.Equal("true", h.Settings.Values[SettingsKeys.TrackingEnabled]);
    }

    [Fact]
    public async Task ALostCameraIsReopenedWithGrowingBackoffUntilItReturns()
    {
        using var h = await FallbackHarness();
        var starts = h.Source.StartCount;

        h.ReportStatus(new CaptureStatus.Failed(new CameraError(CameraErrorKind.DeviceUnavailable, "gone")));
        h.Source.FailNextStarts(NotReady, NotReady, NotReady);
        h.Advance(1);
        Assert.Equal(starts + 1, h.Source.StartCount); // After 1 s.
        h.Advance(2);
        Assert.Equal(starts + 2, h.Source.StartCount); // Then 2 s.
        h.Advance(5);
        Assert.Equal(starts + 3, h.Source.StartCount); // Then 5 s.
        h.Advance(10);
        Assert.Equal(starts + 4, h.Source.StartCount); // Then 10 s; this one succeeds.
        Assert.Equal(new CaptureStatus.Running(External), h.Controller.Status);
    }
}
