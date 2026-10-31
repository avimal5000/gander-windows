using Gander.Core.Controller;
using Gander.Core.Platform;
using Gander.Core.Runtime;

namespace Gander.Core.Tests.Controller;

/// <summary>Port of the Mac TrackingStatusTests ("Tracking status"), without the Accessibility rows and the SF Symbol checks.</summary>
public class TrackingStatusTests
{
    private static TrackingStatus Status(Func<TrackingStatusSnapshot, TrackingStatusSnapshot>? edit = null)
    {
        var snapshot = new TrackingStatusSnapshot(
            CameraAuthorization.Authorized, new CaptureStatus.Running(Fixtures.BuiltInCamera), true, false, 2, true, false, true);
        return TrackingStatus.Evaluate(edit?.Invoke(snapshot) ?? snapshot);
    }

    private static CaptureStatus Failed => new CaptureStatus.Failed(new CameraError(CameraErrorKind.NoDevice));

    [Fact]
    public void EachRowOfTheTableHasItsLine()
    {
        var rows = new (TrackingStatus Status, string Line)[]
        {
            (Status(s => s with { Authorization = CameraAuthorization.Denied }), "Camera access needed"),
            (Status(s => s with { Authorization = CameraAuthorization.Restricted }), "Camera access needed"),
            (Status(s => s with { Capture = Failed }), "Camera unavailable"),
            (Status(s => s with { TrackingEnabled = false }), "Paused"),
            (Status(s => s with { CalibrationRunning = true }), "Calibrating"),
            (Status(s => s with { DisplayCount = 1 }), "Only one display"),
            (Status(s => s with { HasCalibration = false }), "Needs calibration"),
            (Status(s => s with { Capture = new CaptureStatus.Suspended(SuspendReason.ScreenLocked) }), "Suspended: screen locked"),
            (Status(s => s with { Capture = new CaptureStatus.Suspended(SuspendReason.SystemSleep) }), "Suspended: system asleep"),
            (Status(s => s with { Capture = new CaptureStatus.Suspended(SuspendReason.DisplaysAsleep) }), "Suspended: displays asleep"),
            (Status(s => s with { Capture = new CaptureStatus.Starting() }), "Starting camera"),
            (Status(s => s with { Capture = new CaptureStatus.Interrupted("Camera is in use by another app") }), "Camera busy"),
            (Status(s => s with { Capture = new CaptureStatus.Interrupted("Camera disconnected") }), "Interrupted: Camera disconnected"),
            (Status(s => s with { FocusLocked = true }), "Focus Lock on"),
            (Status(), "Tracking"),
            (Status(s => s with { HasFace = false }), "No face"),
        };
        foreach (var (status, line) in rows)
        {
            Assert.Equal(line, status.Line);
        }
    }

    [Fact]
    public void TheCasesAreTheOnesTheTableNames()
    {
        Assert.Equal(TrackingStatusKind.CameraBlocked, Status(s => s with { Authorization = CameraAuthorization.Denied }).Kind);
        Assert.Equal(TrackingStatusKind.CameraFailed, Status(s => s with { Capture = Failed }).Kind);
        Assert.Equal(TrackingStatus.Paused, Status(s => s with { TrackingEnabled = false }));
        Assert.Equal(TrackingStatusKind.Calibrating, Status(s => s with { CalibrationRunning = true }).Kind);
        Assert.Equal(TrackingStatusKind.OneDisplay, Status(s => s with { DisplayCount = 1 }).Kind);
        Assert.Equal(TrackingStatusKind.NeedsCalibration, Status(s => s with { HasCalibration = false }).Kind);
        Assert.Equal(
            TrackingStatus.Suspended(SuspendReason.ScreenLocked),
            Status(s => s with { Capture = new CaptureStatus.Suspended(SuspendReason.ScreenLocked) }));
        Assert.Equal(TrackingStatus.Starting, Status(s => s with { Capture = new CaptureStatus.Starting() }));
        Assert.Equal(TrackingStatus.CameraBusy("x"), Status(s => s with { Capture = new CaptureStatus.Interrupted("x") }));
        Assert.Equal(TrackingStatus.FocusLock, Status(s => s with { FocusLocked = true }));
        Assert.Equal(TrackingStatus.Tracking, Status());
        Assert.Equal(TrackingStatus.NoFace, Status(s => s with { HasFace = false }));
    }

    [Fact]
    public void CameraBlockedBeatsCameraFailed() =>
        Assert.Equal(
            TrackingStatusKind.CameraBlocked,
            Status(s => s with { Authorization = CameraAuthorization.Denied, Capture = Failed }).Kind);

    [Fact]
    public void ABlockedCameraOnlyCountsWhileTrackingIsOn()
    {
        Assert.Equal(TrackingStatus.Paused, Status(s => s with { Authorization = CameraAuthorization.Denied, TrackingEnabled = false }));
        Assert.Equal(TrackingStatus.Paused, Status(s => s with { Authorization = CameraAuthorization.Restricted, TrackingEnabled = false }));
        Assert.Equal(TrackingStatus.Tracking, Status(s => s with { Authorization = CameraAuthorization.NotDetermined }));
    }

    [Fact]
    public void CameraFailedBeatsPausedBecauseTheFailureTurnedTrackingOff() =>
        Assert.Equal(TrackingStatusKind.CameraFailed, Status(s => s with { TrackingEnabled = false, Capture = Failed }).Kind);

    [Fact]
    public void CalibratingShowsEvenWithTrackingOffAndBeatsTheRowsBelowIt()
    {
        Assert.Equal(TrackingStatusKind.Calibrating, Status(s => s with { TrackingEnabled = false, CalibrationRunning = true }).Kind);
        Assert.Equal(
            TrackingStatusKind.Calibrating,
            Status(s => s with { CalibrationRunning = true, DisplayCount = 1, HasCalibration = false }).Kind);
    }

    [Fact]
    public void PausedBeatsEverythingBelowIt() =>
        Assert.Equal(
            TrackingStatus.Paused,
            Status(s => s with { TrackingEnabled = false, DisplayCount = 1, HasCalibration = false, FocusLocked = true }));

    [Fact]
    public void OneDisplayBeatsNeedsCalibration()
    {
        Assert.Equal(TrackingStatusKind.OneDisplay, Status(s => s with { DisplayCount = 1, HasCalibration = false }).Kind);
        Assert.Equal(TrackingStatusKind.OneDisplay, Status(s => s with { DisplayCount = 0 }).Kind);
    }

    [Fact]
    public void NeedsCalibrationBeatsTheCameraStates()
    {
        Assert.Equal(
            TrackingStatusKind.NeedsCalibration,
            Status(s => s with { HasCalibration = false, Capture = new CaptureStatus.Suspended(SuspendReason.ScreenLocked) }).Kind);
        Assert.Equal(
            TrackingStatusKind.NeedsCalibration,
            Status(s => s with { HasCalibration = false, Capture = new CaptureStatus.Running(null) }).Kind);
    }

    [Fact]
    public void SuspendedStartingAndBusyBeatFocusLock()
    {
        Assert.Equal(
            TrackingStatus.Suspended(SuspendReason.SystemSleep),
            Status(s => s with { FocusLocked = true, Capture = new CaptureStatus.Suspended(SuspendReason.SystemSleep) }));
        Assert.Equal(TrackingStatus.Starting, Status(s => s with { FocusLocked = true, Capture = new CaptureStatus.Starting() }));
        Assert.Equal(
            TrackingStatus.CameraBusy("x"),
            Status(s => s with { FocusLocked = true, Capture = new CaptureStatus.Interrupted("x") }));
    }

    [Fact]
    public void FocusLockBeatsTrackingAndNoFace() =>
        Assert.Equal(TrackingStatus.FocusLock, Status(s => s with { FocusLocked = true, HasFace = false }));

    [Fact]
    public void AnotherUsersSessionIsSuspendedWithItsOwnText()
    {
        var session = Status(s => s with { Capture = new CaptureStatus.Suspended(SuspendReason.SessionInactive) });
        Assert.Equal(TrackingStatus.Suspended(SuspendReason.SessionInactive), session);
        Assert.Equal("Suspended: another user's session", session.Line);
    }

    [Fact]
    public void AnUnpluggedChosenCameraNamesItselfWhileCaptureIsInterrupted()
    {
        var waiting = Status(s => s with { Capture = new CaptureStatus.Interrupted("Camera disconnected"), DisconnectedCamera = "Logitech C922" });
        Assert.Equal(TrackingStatus.CameraDisconnected("Logitech C922"), waiting);
        Assert.Equal("Logitech C922 disconnected", waiting.Line);
        Assert.Equal(StatusTone.Problem, waiting.Tone);
        Assert.Equal(TrayIconState.Attention, waiting.Icon);
    }

    [Fact]
    public void TrackingTurnedOffForAnUnpluggedCameraNamesItInsteadOfPaused()
    {
        var lost = Status(s => s with { TrackingEnabled = false, Capture = new CaptureStatus.Stopped(), DisconnectedCamera = "Logitech C922" });
        Assert.Equal(TrackingStatus.CameraDisconnected("Logitech C922"), lost);
        Assert.Equal("Logitech C922 disconnected", lost.Line);
        Assert.Equal(TrayIconState.Attention, lost.Icon);
        Assert.Equal(TrackingStatus.Paused, Status(s => s with { TrackingEnabled = false, Capture = new CaptureStatus.Stopped() }));
    }

    [Fact]
    public void ARunningCalibrationOrAFailedCameraBeatsTheLostCameraWhileTrackingIsOff()
    {
        Assert.Equal(
            TrackingStatusKind.Calibrating,
            Status(s => s with { TrackingEnabled = false, DisconnectedCamera = "Logitech C922", CalibrationRunning = true }).Kind);
        Assert.Equal(
            TrackingStatusKind.CameraFailed,
            Status(s => s with { TrackingEnabled = false, DisconnectedCamera = "Logitech C922", Capture = Failed }).Kind);
    }

    [Fact]
    public void TheUnpluggedCameraOnlyCountsWhileCaptureIsInterruptedAndNothingBeatsItAbove()
    {
        Assert.Equal(TrackingStatus.Tracking, Status(s => s with { DisconnectedCamera = "Logitech C922" }));
        Assert.Equal(
            TrackingStatus.Suspended(SuspendReason.SystemSleep),
            Status(s => s with { Capture = new CaptureStatus.Suspended(SuspendReason.SystemSleep), DisconnectedCamera = "Logitech C922" }));
        Assert.Equal(
            TrackingStatusKind.NeedsCalibration,
            Status(s => s with { Capture = new CaptureStatus.Interrupted("x"), DisconnectedCamera = "Logitech C922", HasCalibration = false }).Kind);
    }

    [Fact]
    public void AStoppedCameraWithTrackingOnAndACalibrationIsAboutToStart() =>
        Assert.Equal(TrackingStatus.Starting, Status(s => s with { Capture = new CaptureStatus.Stopped() }));

    [Fact]
    public void OnlyTheCameraServicesInUseWordingCountsAsAnotherAppHavingTheCamera()
    {
        Assert.True(TrackingStatus.IsUsedByAnotherApp("Camera is in use by another app"));
        Assert.False(TrackingStatus.IsUsedByAnotherApp("No frames received from the camera"));
        Assert.False(TrackingStatus.IsUsedByAnotherApp("Camera disconnected"));
    }

    [Fact]
    public void TheToneFollowsTheSeverity()
    {
        Assert.Equal(StatusTone.Problem, new TrackingStatus(TrackingStatusKind.CameraBlocked).Tone);
        Assert.Equal(StatusTone.Problem, TrackingStatus.CameraBusy("x").Tone);
        Assert.Equal(StatusTone.Idle, TrackingStatus.Paused.Tone);
        Assert.Equal(StatusTone.Attention, new TrackingStatus(TrackingStatusKind.NeedsCalibration).Tone);
        Assert.Equal(StatusTone.Active, TrackingStatus.Tracking.Tone);
        Assert.Equal(StatusTone.Active, TrackingStatus.FocusLock.Tone);
    }

    [Fact]
    public void TheTrayIconFollowsTheTableOfSpecF25()
    {
        Assert.Equal(TrayIconState.Tracking, TrackingStatus.Tracking.Icon);
        Assert.Equal(TrayIconState.Tracking, TrackingStatus.NoFace.Icon);
        Assert.Equal(TrayIconState.Tracking, TrackingStatus.Starting.Icon);
        Assert.Equal(TrayIconState.Tracking, new TrackingStatus(TrackingStatusKind.Calibrating).Icon);
        Assert.Equal(TrayIconState.Paused, TrackingStatus.Paused.Icon);
        Assert.Equal(TrayIconState.Paused, new TrackingStatus(TrackingStatusKind.OneDisplay).Icon);
        Assert.Equal(TrayIconState.Paused, TrackingStatus.Suspended(SuspendReason.ScreenLocked).Icon);
        Assert.Equal(TrayIconState.FocusLock, TrackingStatus.FocusLock.Icon);
        Assert.Equal(TrayIconState.Attention, new TrackingStatus(TrackingStatusKind.CameraBlocked).Icon);
        Assert.Equal(TrayIconState.Attention, new TrackingStatus(TrackingStatusKind.CameraFailed).Icon);
        Assert.Equal(TrayIconState.Attention, TrackingStatus.CameraBusy("x").Icon);
        Assert.Equal(TrayIconState.Attention, new TrackingStatus(TrackingStatusKind.NeedsCalibration).Icon);
    }
}
