using Sway.Core.Calibration;
using Sway.Core.Controller;
using Sway.Core.Platform;
using Sway.Core.Tests.Controller;

namespace Sway.Core.Tests.Calibration;

/// <summary>Ports of the Mac AppModelSetupTests and AppModelWelcomeTests that exist on Windows.</summary>
public sealed class SetupSessionTests
{
    private static SetupSession Make(Harness h) =>
        new(h.Controller, h.Environment.MainThread, h.Scheduler, () => h.Clock.Now, () => DateTimeOffset.FromUnixTimeSeconds(1_700_000_500));

    private static HeadPose Framed(double height = 0.4) =>
        new(0, 0, 0, 0.95, FaceBounds: new FaceBounds(0.5 - (height / 2), 0.5 - (height / 2), height, height));

    private static async Task<(Harness, SetupSession)> OpenOnCameraCheck()
    {
        var h = Harness.Make(autoRun: true, calibrated: true);
        var setup = Make(h);
        setup.Open();
        h.Drain();
        h.Advance(0.125);
        await Task.CompletedTask;
        return (h, setup);
    }

    [Fact]
    public void OpensAtNeedsYouWhenTheCameraIsDeniedAndMovesOnWhenItIsAllowed()
    {
        using var h = Harness.Make(authorization: CameraAuthorization.Denied, calibrated: true);
        using var setup = Make(h);
        setup.Open();
        Assert.Equal(SetupStep.NeedsYou, setup.Step);
        Assert.Contains("Privacy & security > Camera", setup.NeedsYouText);
        Assert.Equal(0, h.Source.StartCount);

        h.Cameras.AuthorizationValue = CameraAuthorization.Authorized;
        h.Advance(1.0625);

        Assert.Equal(SetupStep.CameraCheck, setup.Step);
    }

    [Fact]
    public void ARestrictedCameraGetsItsOwnWording()
    {
        using var h = Harness.Make(authorization: CameraAuthorization.Restricted, calibrated: true);
        using var setup = Make(h);
        setup.Open();
        Assert.Equal(SetupStep.NeedsYou, setup.Step);
        Assert.Equal(SetupPresentation.CameraRestrictedMessage, setup.NeedsYouText);
    }

    [Fact]
    public void ARecalibrationOpensAtTheCameraCheckAndFirstSetupAtTheWelcome()
    {
        using var calibrated = Harness.Make(calibrated: true);
        using var recal = Make(calibrated);
        recal.Open();
        Assert.Equal(SetupStep.CameraCheck, recal.Step);

        using var fresh = Harness.Make();
        using var first = Make(fresh);
        first.Open();
        Assert.Equal(SetupStep.Welcome, first.Step);
        Assert.False(fresh.Controller.CalibrationRunning);
        Assert.Equal(0, fresh.Source.StartCount);
    }

    [Fact]
    public void GetStartedReadsThePermissionAgainAndStartsTheCamera()
    {
        using var h = Harness.Make(authorization: CameraAuthorization.Denied, autoRun: true);
        using var setup = Make(h);
        setup.Open();
        Assert.Equal(SetupStep.Welcome, setup.Step);

        h.Cameras.AuthorizationValue = CameraAuthorization.Authorized;
        setup.GetStarted();
        h.Drain();

        Assert.Equal(SetupStep.CameraCheck, setup.Step);
        Assert.True(h.Controller.CalibrationRunning);
        Assert.Equal(1, h.Source.StartCount);
        setup.GetStarted();
        Assert.Equal(SetupStep.CameraCheck, setup.Step);
    }

    [Fact]
    public async Task TheCameraRunsForTheCameraCheckAndTrackingStaysOff()
    {
        var (h, setup) = await OpenOnCameraCheck();
        using var _h = h;
        using var _s = setup;

        Assert.True(h.Controller.CalibrationRunning);
        Assert.False(h.Controller.TrackingEnabled);
        Assert.False(h.Controller.IsTrackingActive);
        Assert.Equal(1, h.Source.StartCount);
    }

    [Fact]
    public async Task ClosingTheWindowStopsTheCameraWhenTrackingIsOff()
    {
        var (h, setup) = await OpenOnCameraCheck();
        using var _h = h;
        using var _s = setup;

        setup.Close();
        h.Drain();

        Assert.False(setup.IsOpen);
        Assert.Null(setup.Step);
        Assert.False(h.Controller.CalibrationRunning);
        Assert.False(h.Controller.IsCameraActive);
    }

    [Fact]
    public async Task TheHintFollowsThePoseAndRefreshesAtMostFiveTimesASecond()
    {
        var (h, setup) = await OpenOnCameraCheck();
        using var _h = h;
        using var _s = setup;
        Assert.Equal(FramingAdvice.NoFace, setup.Framing);

        h.Emit(new PoseSourceEvent.Sample(new TimedPose(10.0, Framed(0.1))));
        Assert.Equal(FramingAdvice.Closer, setup.Framing);

        h.Emit(new PoseSourceEvent.Sample(new TimedPose(10.1, Framed())));
        Assert.Equal(FramingAdvice.Closer, setup.Framing);

        h.Emit(new PoseSourceEvent.Sample(new TimedPose(10.25, Framed())));
        Assert.Equal(FramingAdvice.Good, setup.Framing);
        Assert.Equal("Looks good.", setup.FramingText);
    }

    [Fact]
    public async Task ASteadyHintDoesNotRaiseChangedAgain()
    {
        var (h, setup) = await OpenOnCameraCheck();
        using var _h = h;
        using var _s = setup;
        h.Emit(new PoseSourceEvent.Sample(new TimedPose(10.0, Framed())));
        var changes = 0;
        setup.Changed += () => changes++;

        h.Emit(new PoseSourceEvent.Sample(new TimedPose(10.3, Framed())));
        h.Emit(new PoseSourceEvent.Sample(new TimedPose(10.6, Framed())));

        Assert.Equal(0, changes);
    }

    [Fact]
    public async Task TheHintIsNotComputedOutsideTheCameraCheckAndStartsOverWhenItBeginsAgain()
    {
        var (h, setup) = await OpenOnCameraCheck();
        using var _h = h;
        using var _s = setup;
        h.Emit(new PoseSourceEvent.Sample(new TimedPose(10.0, Framed())));
        Assert.Equal(FramingAdvice.Good, setup.Framing);

        await setup.StartCalibrationAsync();
        h.Advance(0.125);
        h.Emit(new PoseSourceEvent.Sample(new TimedPose(11.0, Framed(0.1))));
        Assert.Equal(FramingAdvice.Good, setup.Framing);

        setup.CancelCalibration();
        h.Drain();
        Assert.Equal(SetupStep.CameraCheck, setup.Step);
        Assert.Equal(FramingAdvice.NoFace, setup.Framing);
    }

    [Fact]
    public async Task StartCalibrationOnlyWorksFromTheCameraCheck()
    {
        using var h = Harness.Make(autoRun: true);
        using var setup = Make(h);
        setup.Open();
        await setup.StartCalibrationAsync();
        Assert.Equal(SetupStep.Welcome, setup.Step);
        Assert.Equal(CalibrationState.Idle, setup.Calibration.State);
    }

    [Fact]
    public async Task WithOneMonitorCalibrationDoesNotStart()
    {
        using var h = Harness.Make(layout: Fixtures.OneDisplay, autoRun: true, calibrated: false);
        using var setup = Make(h);
        setup.Open();
        setup.GetStarted();
        h.Drain();

        await setup.StartCalibrationAsync();

        Assert.Equal(SetupStep.CameraCheck, setup.Step);
        Assert.False(setup.CanStartCalibration);
        Assert.Equal("Connect a second monitor to calibrate.", setup.Message!.Text);
    }

    [Fact]
    public async Task AFullSetupRunSavesTheCalibrationShowsTheResultAndReleasesTheCamera()
    {
        var (h, setup) = await OpenOnCameraCheck();
        using var _h = h;
        using var _s = setup;
        h.Controller.RemoveFromLibrary(h.Controller.Library.Profiles[0].Key);
        Assert.Empty(h.Controller.Library.Profiles);

        await setup.StartCalibrationAsync();
        h.Advance(0.125);
        Assert.Equal(SetupStep.Calibrating, setup.Step);
        Assert.Equal(CalibrationState.Running, setup.Calibration.State);

        CalibrationSessionTests.Play(h, setup.Calibration, CalibrationSessionTests.Spread);
        h.Drain();

        Assert.Equal(SetupStep.Done, setup.Step);
        Assert.Equal(CalibrationQuality.Good, setup.Result!.Quality);
        Assert.Single(h.Controller.Library.Profiles);
        Assert.False(h.Controller.CalibrationRunning);
        Assert.Equal(1, h.Source.StartCount);
    }

    [Fact]
    public async Task EscapeCancelsBackToTheCameraCheckAndCalibrationCanBeRepeated()
    {
        var (h, setup) = await OpenOnCameraCheck();
        using var _h = h;
        using var _s = setup;
        await setup.StartCalibrationAsync();
        h.Advance(2);

        setup.CancelCalibration();
        h.Drain();

        Assert.Equal(SetupStep.CameraCheck, setup.Step);
        Assert.Equal("Calibration cancelled.", setup.Message!.Text);
        Assert.True(h.Controller.CalibrationRunning, "the camera check still needs the camera");

        await setup.StartCalibrationAsync();
        h.Advance(0.125);
        CalibrationSessionTests.Play(h, setup.Calibration, CalibrationSessionTests.Spread);
        h.Drain();
        Assert.Equal(SetupStep.Done, setup.Step);
    }

    [Fact]
    public async Task AFailedFitStillShowsTheResultStepAndCalibrateAgainGoesBack()
    {
        var (h, setup) = await OpenOnCameraCheck();
        using var _h = h;
        using var _s = setup;
        var before = h.Controller.Library;
        await setup.StartCalibrationAsync();
        h.Advance(0.125);

        CalibrationSessionTests.Play(h, setup.Calibration, _ => null);
        h.Drain();

        Assert.Equal(SetupStep.Done, setup.Step);
        Assert.Equal(CalibrationQuality.Failed, setup.Result!.Quality);
        Assert.Equal(before, h.Controller.Library);
        Assert.False(h.Controller.CalibrationRunning);

        setup.CalibrateAgain();
        h.Drain();
        Assert.Equal(SetupStep.CameraCheck, setup.Step);
        Assert.Null(setup.Result);
        Assert.True(h.Controller.CalibrationRunning);
    }

    [Fact]
    public async Task ClosingMidCalibrationCancelsItAndStopsTheCamera()
    {
        var (h, setup) = await OpenOnCameraCheck();
        using var _h = h;
        using var _s = setup;
        await setup.StartCalibrationAsync();
        h.Advance(2);

        setup.Close();
        h.Drain();

        Assert.False(setup.Calibration.IsActive);
        Assert.False(h.Controller.CalibrationRunning);
        Assert.False(h.Controller.IsCameraActive);
        Assert.DoesNotContain(h.Controller.Library.Profiles, p => p.CreatedAt > DateTimeOffset.FromUnixTimeSeconds(1_700_000_000));
    }

    [Fact]
    public async Task StartTrackingIsTheUsersChoiceSavedAndClosesTheFlow()
    {
        var (h, setup) = await OpenOnCameraCheck();
        using var _h = h;
        using var _s = setup;

        await setup.StartTrackingAsync();
        h.Drain();

        Assert.True(h.Controller.TrackingEnabled);
        Assert.Equal("true", h.Settings.Values[SettingsKeys.TrackingEnabled]);
        Assert.False(setup.IsOpen);
    }

    [Fact]
    public async Task ACameraThatFailedToStartCanBeTriedAgain()
    {
        using var h = Harness.Make(calibrated: true);
        h.Source.FailNextStarts(new CameraError(CameraErrorKind.NoDevice));
        using var setup = Make(h);
        setup.Open();
        h.Drain();
        h.Advance(0.125);
        Assert.False(h.Controller.CalibrationRunning);

        h.Source.AutoRunDevice = Fixtures.BuiltInCamera;
        await setup.RetryCameraAsync();
        h.Drain();

        Assert.True(h.Controller.CalibrationRunning);
        Assert.Equal(2, h.Source.StartCount);
    }
}
