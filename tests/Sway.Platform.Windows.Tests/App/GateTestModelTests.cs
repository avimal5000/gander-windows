using Sway.App.Presentation;
using Sway.Core;
using Sway.Core.Calibration;
using Sway.Core.Platform;

namespace Sway.Platform.Windows.Tests.App;

public sealed class GateTestModelTests
{
    private static DisplayInfo Display(string id, string name, double x, bool builtIn = false) =>
        new(id, 1, name, new DisplayRect(x, 0, 1920, 1080), builtIn, x == 0);

    private static readonly DisplayLayout Two = new([Display("a", "Left", 0, builtIn: true), Display("b", "Right", 1920)]);

    private static readonly CaptureStatus Running = new CaptureStatus.Running(null);

    private double _now;

    private GateRun NewRun() => new(() => _now, () => DateTimeOffset.UnixEpoch.AddSeconds(_now));

    private static TimedPose Pose(double t, double yaw) => new(t, new HeadPose(yaw, 0, 0));

    // Runs a whole one-round test, looking at "a" around -20 degrees and "b" around +20.
    private GateRun RunToTheEnd(double leftYaw = -20, double rightYaw = 20)
    {
        var run = NewRun();
        Assert.True(run.Begin(Two, "Cam", new GateTestConfiguration(1, 3), Running));
        for (var step = 0; step < 2; step++)
        {
            var yaw = step == 0 ? leftYaw : rightYaw;
            Assert.Equal(GateRunState.Running, run.State);
            _now += GateTestConfiguration.SettleDuration;
            run.Tick();
            for (var i = 0; i < 30; i++)
            {
                _now += 0.1;
                run.Ingest(Pose(_now, yaw + ((i % 3) - 1)));
            }

            _now += 0.1;
            run.Tick();
        }

        return run;
    }

    [Fact]
    public void SetupWordingMatchesTheMac()
    {
        Assert.Equal("Head-pose gate test", GateTestWording.Heading);
        Assert.Contains("Nothing leaves your PC.", GateTestWording.Intro, StringComparison.Ordinal);
        Assert.Equal("Rounds: 2", GateTestWording.RoundsText(2));
        Assert.Equal("Hold time: 5 s per display", GateTestWording.HoldText(5));
        Assert.StartsWith("About 26 s, including a short countdown before each hold.", GateTestWording.EstimateText(new GateTestConfiguration(2, 5), 2), StringComparison.Ordinal);
        Assert.Equal("Gander v1 will track 3 displays. This test measures all 4 connected displays.", GateTestWording.TrackedNote(4));
        Assert.Equal("The gate test needs at least 2 displays. Connect another display to continue.", GateTestWording.NeedsTwoDisplays);
    }

    [Fact]
    public void DisplayRowsAreNumberedLeftToRightWithTheirSize()
    {
        var rows = GateTestWording.DisplayRows(Two);
        Assert.Equal(new GateDisplayRow(1, "Left", "1920 × 1080 px, built-in, main", 0), rows[0]);
        Assert.Equal(new GateDisplayRow(2, "Right", "1920 × 1080 px", 1), rows[1]);
    }

    [Fact]
    public void RunningWordingNamesTheRoundAndTheStep()
    {
        var progress = new GateProgress(GatePhase.Hold, 3, 4, 2, 2, 1, 2, Two.Displays[0], 1, 3, 7, 6);
        Assert.Equal("Round 2 of 2 · Display 1 of 2", GateTestWording.StepHeading(progress));
        Assert.Equal("Look at the center of Left", GateTestWording.Instruction("Left"));
        Assert.Equal("Hold still", GateTestWording.PhaseText(GatePhase.Hold));
        Assert.Equal("Get ready…", GateTestWording.PhaseText(GatePhase.Settle));
        Assert.Equal("7 samples · 6 with a face", GateTestWording.SamplesText(progress));
        Assert.Equal("Step 3 of 4", GateTestWording.StepText(progress));
    }

    [Fact]
    public void VerdictsUseTheMacBadges()
    {
        Assert.Equal("GO", GateTestWording.VerdictTitle(SeparabilityVerdict.SeparatesClearly));
        Assert.Equal("MARGINAL", GateTestWording.VerdictTitle(SeparabilityVerdict.Marginal));
        Assert.Equal("NO-GO", GateTestWording.VerdictTitle(SeparabilityVerdict.DoesNotSeparate));
        Assert.Equal("INSUFFICIENT DATA", GateTestWording.VerdictTitle(SeparabilityVerdict.InsufficientData));
        Assert.Equal("Head pose separates your displays reliably.", GateTestWording.VerdictExplanation(SeparabilityVerdict.SeparatesClearly));
    }

    [Fact]
    public void OneDisplayCannotRunAndSaysSo()
    {
        var run = NewRun();
        Assert.False(GateRun.CanRun(new DisplayLayout([Display("a", "Only", 0)])));
        Assert.False(run.Begin(new DisplayLayout([Display("a", "Only", 0)]), null, new GateTestConfiguration(), Running));
        Assert.Equal(GateRunState.Idle, run.State);
        Assert.True(run.MessageIsError);
        Assert.Equal(GateTestWording.NeedsTwoDisplaysOnStart, run.Message);
    }

    [Fact]
    public void WaitsForTheCameraThenRunsAndTimesOut()
    {
        var run = NewRun();
        Assert.True(run.Begin(Two, "Cam", new GateTestConfiguration(1, 3), new CaptureStatus.Starting()));
        Assert.Equal(GateRunState.StartingCamera, run.State);
        Assert.Null(run.Progress);
        _now = 5;
        run.Tick();
        Assert.Equal(GateRunState.StartingCamera, run.State);
        run.OnStatus(Running);
        Assert.Equal(GateRunState.Running, run.State);
        Assert.Equal(GatePhase.Settle, run.Progress!.Phase);
        Assert.Equal("Left", run.Progress.Display.Name);

        var late = NewRun();
        late.Begin(Two, null, new GateTestConfiguration(1, 3), new CaptureStatus.Starting());
        _now += GateRun.CameraStartTimeout;
        late.Tick();
        Assert.Equal(GateRunState.Idle, late.State);
        Assert.Equal(GateTestWording.CameraTimeout, late.Message);
        Assert.True(late.MessageIsError);
    }

    [Fact]
    public void ACameraFailureWhileStartingAbortsWithItsReason()
    {
        var run = NewRun();
        run.Begin(Two, null, new GateTestConfiguration(1, 3), new CaptureStatus.Starting());
        run.OnStatus(new CaptureStatus.Failed(new CameraError(CameraErrorKind.NoDevice)));
        Assert.Equal(GateRunState.Idle, run.State);
        Assert.Equal("The camera failed: no camera was found.", run.Message);
    }

    [Fact]
    public void TheCameraStoppingMidRunAbortsAndKeepsNothing()
    {
        var run = NewRun();
        run.Begin(Two, null, new GateTestConfiguration(1, 3), Running);
        run.OnStatus(new CaptureStatus.Interrupted("in use"));
        Assert.Equal(GateRunState.Idle, run.State);
        Assert.Null(run.Report);
        Assert.StartsWith("The camera stopped delivering frames (", run.Message, StringComparison.Ordinal);
        Assert.EndsWith("Try again.", run.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CancelDiscardsTheRunAndSaysSo()
    {
        var run = NewRun();
        run.Begin(Two, null, new GateTestConfiguration(1, 3), Running);
        run.Cancel();
        Assert.Equal(GateRunState.Idle, run.State);
        Assert.Equal("Gate test cancelled.", run.Message);
        Assert.False(run.MessageIsError);
        Assert.Null(run.Report);
        run.Cancel(); // idle: nothing changes
        run.Ingest(Pose(0, 0));
        Assert.Null(run.Report);
    }

    [Fact]
    public void SamplesCountOnlyDuringTheHoldAndTheRunFinishesWithAReport()
    {
        var run = RunToTheEnd();
        Assert.Equal(GateRunState.Idle, run.State);
        var report = run.Report;
        Assert.NotNull(report);
        Assert.Equal(SeparabilityVerdict.SeparatesClearly, report!.Result.Verdict);
        Assert.Equal("GO", report.Title);
        Assert.Equal(2, report.Result.Labels.Count);
        Assert.All(report.Result.Labels, l => Assert.InRange(l.Count, 28, 30));
        Assert.Equal("Left", report.NameOf("a"));
        Assert.Equal(1.0, report.Result.FaceRate, 3);
    }

    [Fact]
    public void TwoOverlappingDisplaysDoNotSeparate()
    {
        var run = RunToTheEnd(leftYaw: 0, rightYaw: 0.5);
        Assert.NotEqual(SeparabilityVerdict.SeparatesClearly, run.Report!.Result.Verdict);
    }

    [Fact]
    public void MarkdownHasTheVerdictTheDisplaysAndThePairs()
    {
        var run = RunToTheEnd();
        var markdown = run.Report!.Markdown();
        Assert.StartsWith("# Gander head-pose gate test", markdown, StringComparison.Ordinal);
        Assert.Contains("**Result: GO** (separates clearly). Head pose separates your displays reliably.", markdown, StringComparison.Ordinal);
        Assert.Contains($"| Left | {run.Report.Result.Labels[0].Count} |", markdown, StringComparison.Ordinal);
        Assert.Contains("| Left and Right |", markdown, StringComparison.Ordinal);
        Assert.Contains("- Displays: 2, rounds: 1, hold: 3 s per display", markdown, StringComparison.Ordinal);
        Assert.Contains("Numbers only: no video or images were recorded.", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("—", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void FlattenLabelsSamplesByDisplayAndMarksNoFaceFrames()
    {
        var trace = new LabeledPoseTrace
        {
            Segments =
            [
                new TraceSegment("a", 1, [Pose(0, 5), new TimedPose(0.1, null)]),
                new TraceSegment(null, 0, [Pose(1, 9)]),
            ],
        };
        var samples = GateRun.Flatten(trace);
        Assert.Equal(2, samples.Count);
        Assert.Equal("a", samples[0].Label);
        Assert.Equal(1, samples[0].Round);
        Assert.True(samples[0].Face);
        Assert.False(samples[1].Face);
    }

    [Fact]
    public void ShowSetupForgetsTheLastResult()
    {
        var run = RunToTheEnd();
        run.ShowSetup();
        Assert.Null(run.Report);
        Assert.Null(run.Message);
    }
}
