using Sway.Core.Calibration;
using Sway.Core.Platform;
using Sway.Core.Tests.Controller;

namespace Sway.Core.Tests.Calibration;

/// <summary>Ports of the Mac AppModelSetupTests / AppModelGateTests calibration-run cases, on a real controller with fakes.</summary>
public sealed class CalibrationSessionTests
{
    private static readonly DateTimeOffset Created = DateTimeOffset.FromUnixTimeSeconds(1_700_000_500);

    internal static CalibrationSession Make(Harness h) =>
        new(h.Controller, h.Environment.MainThread, h.Scheduler, () => h.Clock.Now, () => Created);

    /// <summary>Starts the session and lets the camera report running.</summary>
    internal static async Task StartRunning(Harness h, CalibrationSession session)
    {
        await session.StartAsync();
        h.Drain();
        h.Advance(0.125);
    }

    /// <summary>Plays a user who looks at the prompted monitor: <paramref name="yawOf"/> gives the yaw per display.</summary>
    internal static void Play(Harness h, CalibrationSession session, Func<DisplayId, double?> yawOf, int maxSteps = 2000)
    {
        var steps = 0;
        while (session.State == CalibrationState.Running && steps++ < maxSteps)
        {
            h.Advance(0.0625);
            if (session.Progress is { } p)
            {
                var yaw = yawOf(p.Display.Id);
                h.Feed(h.Clock.Now, face: yaw is not null, yaw: (yaw ?? 0) + ((steps % 3) - 1) * 0.5);
            }
        }
    }

    internal static double? Spread(DisplayId id) => id == Fixtures.LeftId ? -20 : id == Fixtures.RightId ? 20 : 60;

    [Fact]
    public async Task AFullCalibrationRunsTwoRoundsSavesTheProfileAndSaysItLooksGood()
    {
        using var h = Harness.Make(autoRun: true);
        using var session = Make(h);
        var prompts = new List<string>();
        var phases = new HashSet<string>();
        session.Changed += () =>
        {
            if (session.Progress is { } p)
            {
                phases.Add($"{p.Round}:{p.Display.Id.RawValue}:{p.Phase}");
                prompts.Add(session.Prompt!);
            }
        };
        await StartRunning(h, session);

        Assert.Equal(CalibrationState.Running, session.State);
        Assert.Equal(Fixtures.LeftId, session.Progress!.Display.Id);
        Assert.Equal(GatePhase.Settle, session.Progress.Phase);
        Assert.Equal(1.5, session.Progress.PhaseDuration);
        Assert.Equal(4, session.Progress.StepCount);
        Assert.Equal("Look at Left", session.Prompt);
        Assert.True(h.Controller.CalibrationRunning);

        Play(h, session, Spread);
        h.Drain();
        await session.CameraReleased;

        Assert.Equal(CalibrationState.Finished, session.State);
        Assert.Equal(CalibrationQuality.Good, session.Result!.Quality);
        Assert.Equal("Calibration looks good.", session.Result.Text);
        foreach (var expected in new[] { "1:display-left:Settle", "1:display-left:Hold", "1:display-right:Settle", "1:display-right:Hold", "2:display-left:Settle", "2:display-right:Hold" })
        {
            Assert.Contains(expected, phases);
        }

        Assert.Contains("Look at Right", prompts);
        Assert.Single(h.Source.Configurations);
        Assert.False(h.Controller.CalibrationRunning);

        var stored = h.StoredLibrary()!;
        Assert.Single(stored.Profiles);
        Assert.Equal(session.SavedProfile, stored.Profiles[0]);
        Assert.Equal(PoseFeatureSet.VisionAngles, stored.Profiles[0].Features);
        Assert.Equal(Fixtures.TwoDisplays.Fingerprint, stored.Profiles[0].LayoutFingerprint);
        Assert.Equal(Fixtures.BuiltInCamera.Name, stored.Profiles[0].CameraName);
        Assert.Equal(Created, stored.Profiles[0].CreatedAt);
        Assert.NotNull(h.Controller.ActiveCalibration);
    }

    [Fact]
    public async Task ADisplayTheCameraNeverSawSavesNothingAndNamesIt()
    {
        using var h = Harness.Make(layout: Fixtures.ThreeDisplays, autoRun: true);
        using var session = Make(h);
        await StartRunning(h, session);

        Play(h, session, id => id == Fixtures.FarId ? null : Spread(id));
        h.Drain();

        Assert.Equal(CalibrationState.Finished, session.State);
        Assert.Equal(CalibrationQuality.Failed, session.Result!.Quality);
        Assert.Contains("while you looked at Far.", session.Result.Text);
        Assert.Null(h.StoredLibrary());
        Assert.Empty(h.Controller.Library.Profiles);
    }

    [Fact]
    public async Task AFailedFitSavesNothingAndSaysWhy()
    {
        using var h = Harness.Make(autoRun: true, calibrated: true);
        var before = h.Controller.Library;
        using var session = Make(h);
        await StartRunning(h, session);

        Play(h, session, _ => null);
        h.Drain();

        Assert.Equal(CalibrationQuality.Failed, session.Result!.Quality);
        Assert.Equal(
            "Not enough face samples on at least two monitors to calibrate. Keep your face in view of the camera and try again.",
            session.Result.Text);
        Assert.Equal(before, h.Controller.Library);
        Assert.Null(session.SavedProfile);
    }

    [Fact]
    public async Task CloseMonitorsAreSavedButNotReportedAsGood()
    {
        using var h = Harness.Make(autoRun: true);
        using var session = Make(h);
        await StartRunning(h, session);

        Play(h, session, id => id == Fixtures.LeftId ? -0.5 : 0.5);
        h.Drain();

        Assert.True(session.Result!.IsSaved);
        Assert.NotEqual(CalibrationQuality.Good, session.Result.Quality);
        Assert.Contains("Left and Right", session.Result.Text);
    }

    [Fact]
    public async Task CancelReturnsWithAnInfoMessageSavesNothingAndIgnoresLaterSamples()
    {
        using var h = Harness.Make(autoRun: true);
        using var session = Make(h);
        await StartRunning(h, session);
        h.Advance(2);

        session.Cancel();
        h.Drain();
        await session.CameraReleased;

        Assert.Equal(CalibrationState.Cancelled, session.State);
        Assert.Equal(new CalibrationMessage(false, "Calibration cancelled."), session.Message);
        Assert.Null(session.Progress);
        Assert.False(h.Controller.CalibrationRunning);
        h.Feed(h.Clock.Now);
        h.Advance(10);
        Assert.Equal(CalibrationState.Cancelled, session.State);
        Assert.Null(session.Result);
        Assert.Null(h.StoredLibrary());
    }

    [Fact]
    public async Task ACancelledRunCanBeStartedAgainAndCompletes()
    {
        using var h = Harness.Make(autoRun: true);
        using var session = Make(h);
        await StartRunning(h, session);
        session.Cancel();
        h.Drain();

        await StartRunning(h, session);
        Assert.Equal(CalibrationState.Running, session.State);
        Assert.Null(session.Message);
        Play(h, session, Spread);
        Assert.Equal(CalibrationState.Finished, session.State);
        Assert.True(session.Result!.IsSaved);
    }

    [Fact]
    public async Task CancelDoesNothingWhenNoRunIsGoing()
    {
        using var h = Harness.Make(autoRun: true);
        using var session = Make(h);
        session.Cancel();
        Assert.Equal(CalibrationState.Idle, session.State);
        Assert.Null(session.Message);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task ACameraInterruptionAbortsTheRunWithAReason()
    {
        using var h = Harness.Make(autoRun: true);
        using var session = Make(h);
        await StartRunning(h, session);
        h.Advance(2);

        h.ReportStatus(new CaptureStatus.Interrupted("in use by another app"));
        h.Advance(0.125);

        Assert.Equal(CalibrationState.Aborted, session.State);
        Assert.True(session.Message!.IsError);
        Assert.StartsWith("The camera stopped delivering frames (interrupted: in use by another app).", session.Message.Text);
        Assert.EndsWith("Calibrate again.", session.Message.Text);
        Assert.Null(h.StoredLibrary());
    }

    [Fact]
    public async Task ASuspendedCameraAlsoAbortsTheRun()
    {
        using var h = Harness.Make(autoRun: true);
        using var session = Make(h);
        await StartRunning(h, session);

        h.RaiseSession(SessionEvent.ScreenLocked);
        h.Advance(0.125);

        Assert.Equal(CalibrationState.Aborted, session.State);
        Assert.True(session.Message!.IsError);
    }

    [Fact]
    public async Task AFailureWhileTheCameraStartsAbortsWithItsReason()
    {
        using var h = Harness.Make();
        h.Source.StartError = new CameraError(CameraErrorKind.NoDevice);
        using var session = Make(h);

        await session.StartAsync();
        h.Drain();
        h.Advance(0.25);
        await session.CameraReleased;

        Assert.Equal(CalibrationState.Aborted, session.State);
        Assert.True(session.Message!.IsError);
        Assert.False(h.Controller.CalibrationRunning);
    }

    [Fact]
    public async Task ACameraThatNeverStartsTimesOut()
    {
        using var h = Harness.Make();
        using var session = Make(h);

        await session.StartAsync();
        Assert.Equal(CalibrationState.StartingCamera, session.State);
        h.Advance(9.5);
        Assert.Equal(CalibrationState.StartingCamera, session.State);
        h.Advance(1);

        Assert.Equal(CalibrationState.Aborted, session.State);
        Assert.Equal(
            "The camera did not start in time. Check that no other app is using it, then try again.", session.Message!.Text);
        Assert.False(h.Controller.CalibrationRunning);
    }

    [Fact]
    public async Task ChangingTheMonitorLayoutCancelsTheRun()
    {
        using var h = Harness.Make(autoRun: true);
        using var session = Make(h);
        await StartRunning(h, session);

        h.Displays.Change(Fixtures.ThreeDisplays);
        h.Drain();
        h.Advance(0.125);

        Assert.Equal(CalibrationState.Cancelled, session.State);
        Assert.Equal("The monitor arrangement changed, so the calibration was cancelled.", session.Message!.Text);
    }

    [Fact]
    public async Task TheRunNeedsAtLeastTwoMonitors()
    {
        using var h = Harness.Make(layout: Fixtures.OneDisplay, autoRun: true);
        using var session = Make(h);

        await session.StartAsync();

        Assert.Equal(CalibrationState.Idle, session.State);
        Assert.Equal(new CalibrationMessage(true, "Connect a second monitor to calibrate."), session.Message);
        Assert.False(h.Controller.CalibrationRunning);
        Assert.Equal(0, h.Source.StartCount);
    }

    [Fact]
    public async Task ASecondStartWhileRunningIsIgnored()
    {
        using var h = Harness.Make(autoRun: true);
        using var session = Make(h);
        await StartRunning(h, session);

        await session.StartAsync();

        Assert.Equal(CalibrationState.Running, session.State);
        Assert.Equal(1, h.Source.StartCount);
    }

    [Fact]
    public async Task ATrackingRunKeepsTheCameraAfterwardsAndTheHostsHoldIsLeftAlone()
    {
        using var h = Harness.Make(autoRun: true, calibrated: true);
        await h.StartTracking();
        h.Drain();
        using var session = Make(h);
        await h.Controller.SetCalibrationRunningAsync(true);
        h.Drain();
        h.Advance(0.125);
        await session.StartAsync();
        h.Advance(0.125);
        Assert.Equal(CalibrationState.Running, session.State);

        session.Cancel();
        h.Drain();

        Assert.True(h.Controller.CalibrationRunning, "the host's hold stays");
    }

    [Fact]
    public async Task PoseObservedReportsEverySampleToSubscribers()
    {
        using var h = Harness.Make(autoRun: true, calibrated: true);
        await h.StartTracking();
        var seen = new List<TimedPose>();
        h.Controller.PoseObserved += seen.Add;

        h.Feed(1.0, yaw: 5);
        h.Feed(1.1, face: false);

        Assert.Equal(2, seen.Count);
        Assert.Equal(5, seen[0].Pose!.Value.Yaw);
        Assert.Null(seen[1].Pose);
    }
}
