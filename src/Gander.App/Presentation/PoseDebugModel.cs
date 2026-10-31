using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Gander.Core;
using Gander.Core.Controller;
using Gander.Core.Platform;

namespace Gander.App.Presentation;

/// <summary>Number formatting for the Pose Debug window (Mac <c>Formatting</c>). Pure, so it is tested.</summary>
public static class PoseFormat
{
    /// <summary>Placeholder for a missing value: an em dash.</summary>
    public const string Missing = "—";

    /// <summary>A signed angle with one decimal, for example "+12.3°"; the placeholder for null.</summary>
    public static string Degrees(double? value) =>
        value is { } v ? string.Create(CultureInfo.InvariantCulture, $"{(v < 0 ? "-" : "+")}{Math.Abs(v):0.0}°") : Missing;

    /// <summary>A 0 to 1 fraction as a whole-number percentage, for example "98%".</summary>
    public static string WholePercent(double fraction) =>
        string.Create(CultureInfo.InvariantCulture, $"{Math.Round(fraction * 100):0}%");

    /// <summary>Frames per second with one decimal, for example "14.9 fps".</summary>
    public static string Fps(double value) => string.Create(CultureInfo.InvariantCulture, $"{value:0.0} fps");

    /// <summary>Whole seconds, for example "26 s".</summary>
    public static string Seconds(double value) => string.Create(CultureInfo.InvariantCulture, $"{Math.Round(value):0} s");
}

/// <summary>One face sample kept for the live plot.</summary>
public sealed record PoseHistoryPoint(double Time, HeadPose Pose);

/// <summary>
/// The last 10 s of face poses for the plots and the last 2 s of sample times for the measured frame rate (Mac
/// <c>PoseHistory</c>). A sample without a face leaves a gap in the plot. A timestamp that goes backwards starts a new
/// timeline.
/// </summary>
public sealed class PoseHistory
{
    /// <summary>Seconds of face poses kept for the plots.</summary>
    public const double PlotWindow = 10;

    /// <summary>Seconds of sample times used for the frame rate.</summary>
    public const double FpsWindow = 2;

    private readonly List<PoseHistoryPoint> _points = [];
    private readonly List<double> _times = [];

    /// <summary>Face samples within the plot window of the newest sample, oldest first.</summary>
    public IReadOnlyList<PoseHistoryPoint> Points => _points;

    /// <summary>The newest sample time; null before the first sample.</summary>
    public double? Newest => _times.Count == 0 ? null : _times[^1];

    /// <summary>Samples per second over the last <see cref="FpsWindow"/> seconds (with and without a face); 0 until two samples.</summary>
    public double Fps
    {
        get
        {
            if (_times.Count < 2 || _times[^1] <= _times[0])
            {
                return 0;
            }

            return (_times.Count - 1) / (_times[^1] - _times[0]);
        }
    }

    /// <summary>Adds a sample and drops what fell out of the windows.</summary>
    public void Append(TimedPose sample)
    {
        if (_times.Count > 0 && sample.Timestamp < _times[^1])
        {
            Reset();
        }

        _times.Add(sample.Timestamp);
        var fpsCutoff = sample.Timestamp - FpsWindow;
        var drop = _times.FindIndex(t => t >= fpsCutoff);
        if (drop > 0)
        {
            _times.RemoveRange(0, drop);
        }

        if (sample.Pose is { } pose)
        {
            _points.Add(new PoseHistoryPoint(sample.Timestamp, pose));
        }

        var plotCutoff = sample.Timestamp - PlotWindow;
        var firstKept = _points.FindIndex(p => p.Time >= plotCutoff);
        if (firstKept > 0)
        {
            _points.RemoveRange(0, firstKept);
        }
        else if (firstKept < 0)
        {
            _points.Clear();
        }
    }

    /// <summary>Forgets everything (the camera stopped or restarted).</summary>
    public void Reset()
    {
        _points.Clear();
        _times.Clear();
    }
}

/// <summary>
/// What the text readouts show: the latest pose and frame rate, refreshed at most 0.2 s apart in sample time (Mac
/// <c>LiveReadout</c>). Numbers changing at the frame rate cannot be read anyway.
/// </summary>
public sealed record LiveReadout
{
    /// <summary>Smallest sample-time gap between two refreshes.</summary>
    public const double RefreshInterval = 0.2;

    /// <summary>The pose at the last refresh; null without a face.</summary>
    public HeadPose? Pose { get; init; }

    /// <summary>The measured frame rate at the last refresh.</summary>
    public double Fps { get; init; }

    /// <summary>The sample time of the last refresh; null before the first.</summary>
    public double? Timestamp { get; init; }

    /// <summary>True when <paramref name="sample"/> should replace this readout: the first one, a face appearing or going, 0.2 s passed, or time going backwards.</summary>
    public bool IsDue(TimedPose sample)
    {
        if (Timestamp is not { } last)
        {
            return true;
        }

        if ((Pose is null) != (sample.Pose is null))
        {
            return true;
        }

        var elapsed = sample.Timestamp - last;
        return elapsed < 0 || elapsed >= RefreshInterval;
    }

    /// <summary>The readout for <paramref name="sample"/>.</summary>
    public static LiveReadout From(TimedPose sample, double fps) => new() { Pose = sample.Pose, Fps = fps, Timestamp = sample.Timestamp };
}

/// <summary>Which angle a plot shows.</summary>
public enum PoseAxis
{
    /// <summary>Left and right.</summary>
    Yaw,

    /// <summary>Up and down.</summary>
    Pitch,
}

/// <summary>A point of a plot: how long ago the sample was (0 is the newest) and the angle in degrees.</summary>
public readonly record struct PlotPoint(double SecondsAgo, double Value);

/// <summary>A calibrated display's band on one axis: the range its samples fell in when calibrating.</summary>
public sealed record PlotBand(string Label, int SeriesIndex, double Low, double High);

/// <summary>What one plot draws.</summary>
public sealed record PlotData(
    PoseAxis Axis,
    IReadOnlyList<IReadOnlyList<PlotPoint>> Segments,
    IReadOnlyList<PlotBand> Bands,
    double Low,
    double High)
{
    /// <summary>True when no face pose is in the window.</summary>
    public bool IsEmpty => Segments.Count == 0;
}

/// <summary>Pure shaping of the history and the calibration into plot data (Mac <c>PlotGeometry</c> and <c>PlotTrail</c>).</summary>
public static class PlotGeometry
{
    /// <summary>A gap in time (seconds) between two face samples that breaks the line.</summary>
    public const double GapSeconds = 0.6;

    /// <summary>A band spans this many standard deviations either side of a display's mean (the Mac's cluster ellipse).</summary>
    public const double BandDeviations = 2;

    /// <summary>A band is never narrower than this many degrees either side of the mean.</summary>
    public const double MinimumHalfWidth = 1;

    /// <summary>The angle of a pose on an axis.</summary>
    public static double ValueOf(HeadPose pose, PoseAxis axis) => axis == PoseAxis.Yaw ? pose.Yaw : pose.Pitch;

    /// <summary>
    /// A padded axis range covering <paramref name="values"/>, rounded outward to multiples of <paramref name="step"/> and never
    /// narrower than <paramref name="minimumSpan"/>; -15 to +15 with no data.
    /// </summary>
    public static (double Low, double High) Domain(IReadOnlyCollection<double> values, double padding = 3, double step = 5, double minimumSpan = 30)
    {
        if (values.Count == 0)
        {
            return (-minimumSpan / 2, minimumSpan / 2);
        }

        var lower = Math.Floor((values.Min() - padding) / step) * step;
        var upper = Math.Ceiling((values.Max() + padding) / step) * step;
        var missing = minimumSpan - (upper - lower);
        if (missing > 0)
        {
            lower -= Math.Floor(missing / 2 / step) * step;
            upper += Math.Ceiling(missing / 2 / step) * step;
            if (upper - lower < minimumSpan)
            {
                upper = lower + minimumSpan;
            }
        }

        return (lower, upper);
    }

    /// <summary>The face samples as line segments, oldest first, broken wherever more than <see cref="GapSeconds"/> passed between two.</summary>
    public static IReadOnlyList<IReadOnlyList<PlotPoint>> Segments(IReadOnlyList<PoseHistoryPoint> points, PoseAxis axis)
    {
        var segments = new List<IReadOnlyList<PlotPoint>>();
        if (points.Count == 0)
        {
            return segments;
        }

        var newest = points[^1].Time;
        var current = new List<PlotPoint>();
        double? previous = null;
        foreach (var point in points)
        {
            if (previous is { } last && point.Time - last > GapSeconds && current.Count > 0)
            {
                segments.Add(current);
                current = [];
            }

            current.Add(new PlotPoint(newest - point.Time, ValueOf(point.Pose, axis)));
            previous = point.Time;
        }

        segments.Add(current);
        return segments;
    }

    /// <summary>One band per calibrated display; empty without a calibration.</summary>
    /// <param name="profile">The active calibration (restricted to the connected displays), or null.</param>
    /// <param name="axis">Yaw or pitch.</param>
    /// <param name="label">The label of a display.</param>
    /// <param name="seriesIndex">The color slot of a display (its position in the layout).</param>
    public static IReadOnlyList<PlotBand> Bands(CalibrationProfile? profile, PoseAxis axis, Func<DisplayId, string> label, Func<DisplayId, int> seriesIndex)
    {
        if (profile is null)
        {
            return [];
        }

        return profile.Displays.Select(d =>
        {
            var mean = axis == PoseAxis.Yaw ? d.Yaw : d.Pitch;
            var std = axis == PoseAxis.Yaw ? d.YawStd : d.PitchStd;
            var half = Math.Max(MinimumHalfWidth, BandDeviations * std);
            return new PlotBand(label(d.DisplayId), seriesIndex(d.DisplayId), mean - half, mean + half);
        }).ToList();
    }

    /// <summary>The plot for an axis: the trail, the bands and a vertical range that covers both.</summary>
    public static PlotData Build(IReadOnlyList<PoseHistoryPoint> points, PoseAxis axis, IReadOnlyList<PlotBand> bands)
    {
        var segments = Segments(points, axis);
        var values = segments.SelectMany(s => s.Select(p => p.Value)).Concat(bands.SelectMany(b => new[] { b.Low, b.High })).ToList();
        var (low, high) = Domain(values);
        return new PlotData(axis, segments, bands, low, high);
    }
}

/// <summary>What the Pose Debug window's text says about the camera and the face.</summary>
public static class PoseDebugWording
{
    /// <summary>The window title.</summary>
    public const string Title = "Gander Pose Debug";

    /// <summary>The note under the readout (no video is shown or kept).</summary>
    public const string PrivacyNote = "Gander never shows or stores video. A trace holds numbers only.";

    /// <summary>Shown while no camera is running.</summary>
    public const string CameraOffHint = "Turn the camera on to record.";

    /// <summary>Over an empty plot with the camera on.</summary>
    public const string WaitingForFace = "Waiting for a face.";

    /// <summary>Over an empty plot with the camera off.</summary>
    public const string CameraOff = "The camera is off.";

    /// <summary>The yaw plot heading.</summary>
    public const string YawHeading = "Last 10 s: yaw";

    /// <summary>The pitch plot heading.</summary>
    public const string PitchHeading = "Last 10 s: pitch";

    /// <summary>Explains the colored bands.</summary>
    public const string BandsNote = "Bands show where each calibrated display's samples fell (two standard deviations).";

    /// <summary>Said when no calibration fits.</summary>
    public const string NoBandsNote = "No calibration fits the connected displays, so there are no bands.";

    /// <summary>The camera line: the device name while it runs, otherwise the state.</summary>
    public static string CameraLine(CaptureStatus status, string? cameraName) => status switch
    {
        CaptureStatus.Running running => running.Device?.Name ?? cameraName ?? "Camera on",
        _ => CaptureText.Summary(status),
    };

    /// <summary>What to say over an empty plot.</summary>
    public static string EmptyPlotText(bool cameraRunning) => cameraRunning ? WaitingForFace : CameraOff;

    /// <summary>The frame rate, or the placeholder while the camera is not running.</summary>
    public static string FpsText(bool cameraRunning, double fps) => cameraRunning ? PoseFormat.Fps(fps) : PoseFormat.Missing;
}

/// <summary>Everything the copied summary says, copied from the window.</summary>
public sealed record PoseDebugSummaryInput(
    string StatusLine,
    string CameraLine,
    double Fps,
    HeadPose? Pose,
    string LookText,
    PlotData Yaw,
    PlotData Pitch,
    int HistoryFaceSamples,
    string? RecordingFile,
    int RecordingSamples,
    DateTimeOffset Now);

/// <summary>Builds the text the "Copy summary" button puts on the clipboard (numbers and app state only).</summary>
public static class PoseDebugSummary
{
    /// <summary>The summary, line by line.</summary>
    public static string Build(PoseDebugSummaryInput input)
    {
        var text = new StringBuilder();
        text.AppendLine("Gander pose debug summary");
        text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"Taken: {input.Now.UtcDateTime:yyyy-MM-dd HH:mm:ss} UTC"));
        text.AppendLine($"Status: {input.StatusLine}");
        text.AppendLine($"Camera: {input.CameraLine}");
        text.AppendLine($"Frame rate: {PoseFormat.Fps(input.Fps)}");
        if (input.Pose is { } pose)
        {
            text.AppendLine("Face: detected");
            text.AppendLine($"Yaw {PoseFormat.Degrees(pose.Yaw)}, pitch {PoseFormat.Degrees(pose.Pitch)}, roll {PoseFormat.Degrees(pose.Roll)}, confidence {PoseFormat.WholePercent(pose.FaceConfidence)}");
        }
        else
        {
            text.AppendLine("Face: not detected");
        }

        text.AppendLine($"Looking at: {input.LookText}");
        text.AppendLine($"Last 10 s: {input.HistoryFaceSamples} face samples");
        AppendRange(text, "Yaw", input.Yaw);
        AppendRange(text, "Pitch", input.Pitch);
        if (input.Yaw.Bands.Count > 0)
        {
            text.AppendLine("Calibrated bands (degrees, mean plus or minus two standard deviations):");
            for (var i = 0; i < input.Yaw.Bands.Count; i++)
            {
                var yaw = input.Yaw.Bands[i];
                var pitch = i < input.Pitch.Bands.Count ? input.Pitch.Bands[i] : null;
                text.Append(string.Create(CultureInfo.InvariantCulture, $"  {yaw.Label}: yaw {yaw.Low:0.0} to {yaw.High:0.0}"));
                text.AppendLine(pitch is null ? string.Empty : string.Create(CultureInfo.InvariantCulture, $", pitch {pitch.Low:0.0} to {pitch.High:0.0}"));
            }
        }
        else
        {
            text.AppendLine("Calibrated bands: none");
        }

        if (input.RecordingFile is { } file)
        {
            text.AppendLine($"Last trace: {file} ({input.RecordingSamples} samples)");
        }

        return text.ToString().TrimEnd();
    }

    private static void AppendRange(StringBuilder text, string name, PlotData data)
    {
        var values = data.Segments.SelectMany(s => s.Select(p => p.Value)).ToList();
        if (values.Count == 0)
        {
            text.AppendLine($"{name}: no samples");
            return;
        }

        text.AppendLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{name}: min {values.Min():0.0}, mean {values.Average():0.0}, max {values.Max():0.0}"));
    }
}

/// <summary>
/// Collects poses for a free recording (Mac <c>TraceRecorder</c>): numbers and a face flag only, never a frame. Times are
/// seconds since the first sample. It stops by itself at <see cref="MaxSamples"/>.
/// </summary>
public sealed class TraceRecorder
{
    /// <summary>Most samples one recording keeps (about ten minutes at 15 fps).</summary>
    public const int MaxSamples = 9000;

    private readonly List<PoseSample> _samples = [];
    private double? _start;

    /// <summary>True between <see cref="Begin"/> and <see cref="Finish"/>.</summary>
    public bool IsRecording { get; private set; }

    /// <summary>Samples collected so far.</summary>
    public int SampleCount => _samples.Count;

    /// <summary>Seconds covered so far.</summary>
    public double Duration => _samples.Count == 0 ? 0 : _samples[^1].TimeSeconds;

    /// <summary>True once <see cref="MaxSamples"/> was reached; the owner should finish the recording.</summary>
    public bool IsFull => _samples.Count >= MaxSamples;

    /// <summary>Starts an empty recording.</summary>
    public void Begin()
    {
        _samples.Clear();
        _start = null;
        IsRecording = true;
    }

    /// <summary>Adds a sample while recording. Frames without a face are kept (angles 0, face flag false).</summary>
    public void Append(TimedPose sample)
    {
        if (!IsRecording || IsFull)
        {
            return;
        }

        _start ??= sample.Timestamp;
        var time = Math.Max(0, sample.Timestamp - _start.Value);
        _samples.Add(sample.Pose is { } pose
            ? new PoseSample(time, pose.Yaw, pose.Pitch, pose.Roll, string.Empty)
            : new PoseSample(time, 0, 0, 0, string.Empty, 0, false));
    }

    /// <summary>Stops recording and returns the trace; null when nothing was recorded.</summary>
    public PoseTrace? Finish()
    {
        IsRecording = false;
        var trace = _samples.Count == 0 ? null : new PoseTrace(_samples.ToList());
        _samples.Clear();
        _start = null;
        return trace;
    }
}
