using Gander.App.Hosting;
using Gander.App.Presentation;
using Gander.Core;
using Gander.Core.Platform;

namespace Gander.Platform.Windows.Tests.App;

public sealed class PoseDebugModelTests
{
    private static TimedPose Face(double t, double yaw = 0, double pitch = 0) => new(t, new HeadPose(yaw, pitch, 1.5, 0.98));

    private static TimedPose NoFace(double t) => new(t, null);

    [Fact]
    public void AnglesAreSignedWithOneDecimalAndDegreeSign()
    {
        Assert.Equal("+12.3\u00B0", PoseFormat.Degrees(12.34));
        Assert.Equal("-4.0\u00B0", PoseFormat.Degrees(-4));
        Assert.Equal("+0.0\u00B0", PoseFormat.Degrees(0));
        Assert.Equal("\u2014", PoseFormat.Degrees(null));
        Assert.Equal("98%", PoseFormat.WholePercent(0.98));
        Assert.Equal("14.9 fps", PoseFormat.Fps(14.94));
        Assert.Equal("26 s", PoseFormat.Seconds(25.6));
    }

    [Fact]
    public void HistoryKeepsTenSecondsOfFacesAndMeasuresFps()
    {
        var history = new PoseHistory();
        for (var i = 0; i <= 300; i++)
        {
            history.Append(i % 2 == 0 ? Face(i / 15.0) : NoFace(i / 15.0));
        }

        Assert.All(history.Points, p => Assert.True(p.Time >= 300 / 15.0 - PoseHistory.PlotWindow));
        Assert.Equal(76, history.Points.Count);
        Assert.Equal(15, history.Fps, 1);
    }

    [Fact]
    public void FpsIsZeroUntilTwoSamplesAndTimeGoingBackwardsRestarts()
    {
        var history = new PoseHistory();
        Assert.Equal(0, history.Fps);
        history.Append(Face(10));
        Assert.Equal(0, history.Fps);
        history.Append(Face(10.5));
        Assert.Equal(2, history.Fps, 3);
        history.Append(Face(1));
        Assert.Single(history.Points);
        Assert.Equal(1, history.Newest);
    }

    [Fact]
    public void ReadoutRefreshesEveryFifthOfASecondAndAtOnceWhenTheFaceAppearsOrGoes()
    {
        var readout = new LiveReadout();
        Assert.True(readout.IsDue(Face(0)));
        readout = LiveReadout.From(Face(0), 15);
        Assert.False(readout.IsDue(Face(0.1)));
        Assert.True(readout.IsDue(Face(0.2)));
        Assert.True(readout.IsDue(NoFace(0.05)));
        Assert.True(readout.IsDue(Face(-1)));
    }

    [Fact]
    public void DomainPadsRoundsOutAndKeepsAMinimumSpan()
    {
        Assert.Equal((-15.0, 15.0), PlotGeometry.Domain([]));
        Assert.Equal((-15.0, 15.0), PlotGeometry.Domain([0.0, 1.0]));
        var wide = PlotGeometry.Domain([-40.0, 25.0]);
        Assert.Equal((-45.0, 30.0), wide);
    }

    [Fact]
    public void SegmentsBreakAtGapsAndCountBackFromTheNewest()
    {
        var points = new List<PoseHistoryPoint>
        {
            new(1.0, new HeadPose(5, 1, 0)),
            new(1.1, new HeadPose(6, 1, 0)),
            new(3.0, new HeadPose(7, 2, 0)),
        };
        var yaw = PlotGeometry.Segments(points, PoseAxis.Yaw);
        Assert.Equal(2, yaw.Count);
        Assert.Equal(2, yaw[0].Count);
        Assert.Equal(new PlotPoint(2.0, 5), yaw[0][0]);
        Assert.Equal(new PlotPoint(0, 7), yaw[1][0]);
        Assert.Equal(2, PlotGeometry.Segments(points, PoseAxis.Pitch)[1][0].Value);
        Assert.Empty(PlotGeometry.Segments([], PoseAxis.Yaw));
    }

    [Fact]
    public void BandsAreMeanPlusOrMinusTwoDeviationsWithAMinimumAndNoneWithoutACalibration()
    {
        Assert.Empty(PlotGeometry.Bands(null, PoseAxis.Yaw, id => id.RawValue, _ => 0));
        var profile = new CalibrationProfile(
            PoseFeatureSet.VisionAngles,
            [new DisplayProfile("left", -20, 2, 4, 0.1, 40), new DisplayProfile("right", 20, -2, 3, 0.1, 40)],
            3,
            3,
            "fp",
            "Cam",
            DateTimeOffset.UnixEpoch);
        var yaw = PlotGeometry.Bands(profile, PoseAxis.Yaw, id => id.RawValue.ToUpperInvariant(), id => id.RawValue == "left" ? 0 : 1);
        Assert.Equal(new PlotBand("LEFT", 0, -28, -12), yaw[0]);
        Assert.Equal(new PlotBand("RIGHT", 1, 14, 26), yaw[1]);
        var pitch = PlotGeometry.Bands(profile, PoseAxis.Pitch, id => id.RawValue, _ => 0);
        Assert.Equal(2, pitch[0].High - pitch[0].Low, 3);
    }

    [Fact]
    public void BuildCoversTheTrailAndTheBands()
    {
        var points = new List<PoseHistoryPoint> { new(0, new HeadPose(60, 0, 0)) };
        var plot = PlotGeometry.Build(points, PoseAxis.Yaw, [new PlotBand("A", 0, -50, -40)]);
        Assert.True(plot.Low <= -50 && plot.High >= 60);
        Assert.False(plot.IsEmpty);
        Assert.True(PlotGeometry.Build([], PoseAxis.Pitch, []).IsEmpty);
    }

    [Fact]
    public void WordingNamesTheCameraAndTheEmptyPlot()
    {
        Assert.Equal("USB Cam", PoseDebugWording.CameraLine(new CaptureStatus.Running(new CameraDevice("a", "USB Cam", false)), "Other"));
        Assert.Equal("Other", PoseDebugWording.CameraLine(new CaptureStatus.Running(null), "Other"));
        Assert.Equal("Camera off", PoseDebugWording.CameraLine(new CaptureStatus.Stopped(), null));
        Assert.Equal("Waiting for a face.", PoseDebugWording.EmptyPlotText(true));
        Assert.Equal("The camera is off.", PoseDebugWording.EmptyPlotText(false));
        Assert.Equal("\u2014", PoseDebugWording.FpsText(false, 15));
        Assert.Equal("15.0 fps", PoseDebugWording.FpsText(true, 15));
    }

    [Fact]
    public void RecorderKeepsNumbersOnlyWithTimesFromTheFirstSampleAndFlagsNoFace()
    {
        var recorder = new TraceRecorder();
        recorder.Append(Face(5)); // not recording yet
        Assert.Equal(0, recorder.SampleCount);
        recorder.Begin();
        recorder.Append(Face(100, 10, -3));
        recorder.Append(NoFace(100.5));
        recorder.Append(Face(101.25, -10, 3));
        Assert.True(recorder.IsRecording);
        Assert.Equal(3, recorder.SampleCount);
        Assert.Equal(1.25, recorder.Duration, 3);

        var trace = recorder.Finish();
        Assert.False(recorder.IsRecording);
        Assert.NotNull(trace);
        Assert.Equal(new PoseSample(0, 10, -3, 1.5, string.Empty), trace!.Samples[0]);
        Assert.False(trace.Samples[1].Face);
        Assert.Equal(0, trace.Samples[1].Yaw);
        Assert.Equal(1.25, trace.Samples[2].TimeSeconds, 3);
        Assert.Equal(3, PoseTrace.FromJson(trace.ToJson()).Samples.Count);
        Assert.Null(new TraceRecorder().Finish());
    }

    [Fact]
    public void RecorderStopsAtTheLimit()
    {
        var recorder = new TraceRecorder();
        recorder.Begin();
        for (var i = 0; i < TraceRecorder.MaxSamples + 10; i++)
        {
            recorder.Append(Face(i));
        }

        Assert.True(recorder.IsFull);
        Assert.Equal(TraceRecorder.MaxSamples, recorder.SampleCount);
    }

    [Fact]
    public void SummaryHasTheReadoutTheRangesAndTheBands()
    {
        var points = new List<PoseHistoryPoint> { new(0, new HeadPose(2, 1, 0)), new(0.1, new HeadPose(4, 3, 0)) };
        var bands = new List<PlotBand> { new("Left", 0, -10, -2) };
        var input = new PoseDebugSummaryInput(
            "Tracking",
            "USB Cam",
            14.9,
            new HeadPose(3, 2, 1, 0.97),
            "Looking at Left",
            PlotGeometry.Build(points, PoseAxis.Yaw, bands),
            PlotGeometry.Build(points, PoseAxis.Pitch, bands),
            2,
            @"C:\traces\a.json",
            120,
            new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.Zero));
        var text = PoseDebugSummary.Build(input);
        Assert.Contains("Taken: 2026-10-31 12:00:00 UTC", text);
        Assert.Contains("Camera: USB Cam", text);
        Assert.Contains("Frame rate: 14.9 fps", text);
        Assert.Contains("Face: detected", text);
        Assert.Contains("confidence 97%", text);
        Assert.Contains("Yaw: min 2.0, mean 3.0, max 4.0", text);
        Assert.Contains("Left: yaw -10.0 to -2.0", text);
        Assert.Contains("Last trace: C:\\traces\\a.json (120 samples)", text);
        var noFace = PoseDebugSummary.Build(input with { Pose = null, Yaw = PlotGeometry.Build([], PoseAxis.Yaw, []), RecordingFile = null });
        Assert.Contains("Face: not detected", noFace);
        Assert.Contains("Yaw: no samples", noFace);
        Assert.Contains("Calibrated bands: none", noFace);
    }

    [Fact]
    public void TraceFilesGoUnderLocalAppDataGanderTracesWithASortableName()
    {
        Assert.EndsWith(Path.Combine("Gander", "Traces"), TraceStore.DefaultDirectory, StringComparison.Ordinal);
        var name = TraceStore.FileName(new DateTimeOffset(2026, 10, 31, 14, 25, 30, TimeSpan.Zero));
        Assert.StartsWith("pose-trace-", name, StringComparison.Ordinal);
        Assert.EndsWith(".json", name, StringComparison.Ordinal);
    }

    [Fact]
    public void StoreWritesTheJsonAndNeverOverwritesAFileFromTheSameSecond()
    {
        var directory = Path.Combine(Path.GetTempPath(), "gander-trace-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new TraceStore(directory);
            var trace = new PoseTrace([new PoseSample(0, 1, 2, 3, string.Empty)]);
            var now = DateTimeOffset.Now;
            var first = store.Write(trace, now);
            var second = store.Write(trace, now);
            Assert.NotEqual(first, second);
            Assert.Single(PoseTrace.FromJson(File.ReadAllText(first)).Samples);
            Assert.DoesNotContain("png", File.ReadAllText(first), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }
}
