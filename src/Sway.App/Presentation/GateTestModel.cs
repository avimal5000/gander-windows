using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Sway.Core;
using Sway.Core.Calibration;
using Sway.Core.Controller;
using Sway.Core.Platform;

namespace Sway.App.Presentation;

/// <summary>One display in the gate test's setup card.</summary>
public sealed record GateDisplayRow(int Number, string Name, string Detail, int SeriesIndex);

/// <summary>The words of the Head-Pose Gate Test window (Mac <c>GateSetupView</c>, <c>GateRunningView</c>, <c>GateReportView</c>). Pure.</summary>
public static class GateTestWording
{
    /// <summary>The window title.</summary>
    public const string Title = "Gander Head-Pose Gate Test";

    /// <summary>The heading inside the window.</summary>
    public const string Heading = "Head-pose gate test";

    /// <summary>What the test does.</summary>
    public const string Intro =
        "A green marker appears on each display in turn. Look at its center and hold still. "
        + "Gander checks whether your head pose tells the displays apart. Nothing leaves your PC.";

    /// <summary>The displays card heading.</summary>
    public const string DisplaysHeading = "Displays (left to right)";

    /// <summary>Shown with no displays.</summary>
    public const string NoDisplays = "No displays found.";

    /// <summary>The settings card heading.</summary>
    public const string SettingsHeading = "Settings";

    /// <summary>Shown when the test cannot run.</summary>
    public const string NeedsTwoDisplays = "The gate test needs at least 2 displays. Connect another display to continue.";

    /// <summary>The same, when Start was pressed.</summary>
    public const string NeedsTwoDisplaysOnStart = "The gate test needs at least 2 displays. Connect another display and try again.";

    /// <summary>Start button.</summary>
    public const string StartButton = "Start test";

    /// <summary>Cancel button.</summary>
    public const string CancelButton = "Cancel";

    /// <summary>Hint under Cancel.</summary>
    public const string EscHint = "Press Esc at any time to cancel.";

    /// <summary>Shown while the camera starts.</summary>
    public const string StartingCamera = "Starting camera…";

    /// <summary>The message after a cancel.</summary>
    public const string Cancelled = "Gate test cancelled.";

    /// <summary>The report heading.</summary>
    public const string ResultHeading = "Gate result";

    /// <summary>Copy button.</summary>
    public const string CopyButton = "Copy Markdown Summary";

    /// <summary>Copy button after a click.</summary>
    public const string CopiedButton = "Copied";

    /// <summary>Run again button.</summary>
    public const string RunAgainButton = "Run Again";

    /// <summary>What the camera failing to start says.</summary>
    public const string CameraTimeout = "The camera did not start in time. Check that no other app is using it, then try again.";

    /// <summary>Appended to a camera that stopped mid-run.</summary>
    public const string TryAgain = "Try again.";

    /// <summary>"Gander v1 will track 3 displays. This test measures all 4 connected displays."</summary>
    public static string TrackedNote(int connected) =>
        $"Gander v1 will track {SwayPolicy.MaxTrackedDisplays} displays. This test measures all {connected} connected displays.";

    /// <summary>"1920 × 1080 px, built-in, main".</summary>
    public static string DisplayDetail(DisplayInfo display)
    {
        var parts = new List<string> { string.Create(CultureInfo.InvariantCulture, $"{(int)display.Frame.Width} × {(int)display.Frame.Height} px") };
        if (display.IsBuiltIn)
        {
            parts.Add("built-in");
        }

        if (display.IsMain)
        {
            parts.Add("main");
        }

        return string.Join(", ", parts);
    }

    /// <summary>The numbered display rows, in layout order.</summary>
    public static IReadOnlyList<GateDisplayRow> DisplayRows(DisplayLayout layout)
    {
        var labels = DisplayLabeler.Labels(layout);
        return layout.Displays.Select((d, i) => new GateDisplayRow(i + 1, labels.GetValueOrDefault(d.Id, d.Name), DisplayDetail(d), i)).ToList();
    }

    /// <summary>"Rounds: 2".</summary>
    public static string RoundsText(int rounds) => $"Rounds: {rounds}";

    /// <summary>"Hold time: 5 s per display".</summary>
    public static string HoldText(double seconds) => $"Hold time: {PoseFormat.Seconds(seconds)} per display";

    /// <summary>"About 26 s, including a short countdown before each hold. ..."</summary>
    public static string EstimateText(GateTestConfiguration configuration, int displayCount) =>
        $"About {PoseFormat.Seconds(configuration.EstimatedDuration(displayCount))}, including a short countdown before each hold. "
        + "The camera turns on for the test and returns to its previous state afterwards.";

    /// <summary>"Round 1 of 2 · Display 1 of 2".</summary>
    public static string StepHeading(GateProgress progress) =>
        $"Round {progress.Round} of {progress.Rounds} · Display {progress.DisplayNumber} of {progress.DisplayCount}";

    /// <summary>"Look at the center of Left".</summary>
    public static string Instruction(string displayName) => $"Look at the center of {displayName}";

    /// <summary>"Get ready…" while settling, "Hold still" while recording.</summary>
    public static string PhaseText(GatePhase phase) => phase == GatePhase.Settle ? "Get ready…" : "Hold still";

    /// <summary>"42 samples · 40 with a face".</summary>
    public static string SamplesText(GateProgress progress) => $"{progress.RecordedSamples} samples · {progress.RecordedFaces} with a face";

    /// <summary>"Step 1 of 4".</summary>
    public static string StepText(GateProgress progress) => $"Step {progress.StepNumber} of {progress.StepCount}";

    /// <summary>The badge text of a verdict.</summary>
    public static string VerdictTitle(SeparabilityVerdict verdict) => verdict switch
    {
        SeparabilityVerdict.SeparatesClearly => "GO",
        SeparabilityVerdict.Marginal => "MARGINAL",
        SeparabilityVerdict.DoesNotSeparate => "NO-GO",
        _ => "INSUFFICIENT DATA",
    };

    /// <summary>One sentence about a verdict.</summary>
    public static string VerdictExplanation(SeparabilityVerdict verdict) => verdict switch
    {
        SeparabilityVerdict.SeparatesClearly => "Head pose separates your displays reliably.",
        SeparabilityVerdict.Marginal => "Separation is borderline. Review the numbers below before deciding.",
        SeparabilityVerdict.DoesNotSeparate => "Head pose alone does not reliably separate these displays.",
        _ => "Not enough face samples to decide. Check lighting and framing, then run again.",
    };

    /// <summary>The camera message for a status that is not running while a run is starting or running.</summary>
    public static string CameraStopped(CaptureStatus status) =>
        $"The camera stopped delivering frames ({CaptureText.Summary(status).ToLowerInvariant()}). {TryAgain}";
}

/// <summary>The finished run: the numbers, the words and the Markdown.</summary>
public sealed class GateReport
{
    /// <summary>Builds a report.</summary>
    /// <param name="result">The separability result; its labels are display ids.</param>
    /// <param name="layout">The displays that were measured.</param>
    /// <param name="cameraName">The camera, if known.</param>
    /// <param name="configuration">Rounds and hold time.</param>
    /// <param name="createdAt">When the run finished.</param>
    public GateReport(SeparabilityResult result, DisplayLayout layout, string? cameraName, GateTestConfiguration configuration, DateTimeOffset createdAt)
    {
        Result = result;
        Layout = layout;
        CameraName = cameraName;
        Configuration = configuration;
        CreatedAt = createdAt;
        Labels = DisplayLabeler.Labels(layout).ToDictionary(p => p.Key.RawValue, p => p.Value);
    }

    /// <summary>The separability result.</summary>
    public SeparabilityResult Result { get; }

    /// <summary>The displays measured.</summary>
    public DisplayLayout Layout { get; }

    /// <summary>The camera, if known.</summary>
    public string? CameraName { get; }

    /// <summary>Rounds and hold time.</summary>
    public GateTestConfiguration Configuration { get; }

    /// <summary>When the run finished.</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>Display id to label.</summary>
    public IReadOnlyDictionary<string, string> Labels { get; }

    /// <summary>The badge text of the verdict.</summary>
    public string Title => GateTestWording.VerdictTitle(Result.Verdict);

    /// <summary>The sentence about the verdict.</summary>
    public string Explanation => GateTestWording.VerdictExplanation(Result.Verdict);

    /// <summary>"LG 27 · 31 Oct 2026, 14:30" style source line.</summary>
    public string SourceLine
    {
        get
        {
            var parts = new List<string> { "Gate test just run" };
            if (!string.IsNullOrEmpty(CameraName))
            {
                parts.Add(CameraName);
            }

            parts.Add(CreatedAt.LocalDateTime.ToString("d MMM yyyy, HH:mm", CultureInfo.InvariantCulture));
            return string.Join(" · ", parts);
        }
    }

    /// <summary>The label of a display id.</summary>
    public string NameOf(string id) => Labels.TryGetValue(id, out var name) ? name : id;

    /// <summary>The report as Markdown, for the Copy button.</summary>
    public string Markdown()
    {
        var inv = CultureInfo.InvariantCulture;
        var text = new StringBuilder();
        text.AppendLine("# Gander head-pose gate test");
        text.AppendLine();
        text.AppendLine(string.Create(inv, $"**Result: {Title}** ({Result.VerdictText}). {Explanation}"));
        text.AppendLine();
        text.AppendLine($"- Run: {SourceLine}");
        text.AppendLine(string.Create(inv, $"- Displays: {Layout.Displays.Count}, rounds: {Configuration.Rounds}, hold: {PoseFormat.Seconds(Configuration.HoldDuration)} per display"));
        text.AppendLine($"- Face found in {PoseFormat.WholePercent(Result.FaceRate)} of frames");
        text.AppendLine(Result.HeldOutAccuracy is { } accuracy
            ? $"- Held-out accuracy: {PoseFormat.WholePercent(accuracy)}"
            : "- Held-out accuracy: not enough data");
        text.AppendLine();
        text.AppendLine("## Displays");
        text.AppendLine();
        text.AppendLine("| Display | Face samples | Yaw (mean, std) | Pitch (mean, std) |");
        text.AppendLine("|---|---|---|---|");
        foreach (var label in Result.Labels)
        {
            text.AppendLine(string.Create(
                inv,
                $"| {NameOf(label.Label)} | {label.Count} | {label.MeanYaw:0.0}°, {label.StdYaw:0.0}° | {label.MeanPitch:0.0}°, {label.StdPitch:0.0}° |"));
        }

        if (Result.Pairs.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("## Pairs");
            text.AppendLine();
            text.AppendLine("| Pair | Distance | Yaw difference | d-prime |");
            text.AppendLine("|---|---|---|---|");
            foreach (var pair in Result.Pairs)
            {
                text.AppendLine(string.Create(
                    inv,
                    $"| {NameOf(pair.First)} and {NameOf(pair.Second)} | {pair.CenterDistanceDegrees:0.0}° | {pair.YawDifferenceDegrees:0.0}° | {pair.DPrime:0.00} |"));
            }
        }

        text.AppendLine();
        text.AppendLine("Numbers only: no video or images were recorded.");
        return text.ToString().TrimEnd();
    }
}

/// <summary>What the gate test is doing.</summary>
public enum GateRunState
{
    /// <summary>Nothing running; the setup form or the last report shows.</summary>
    Idle,

    /// <summary>Waiting for the camera.</summary>
    StartingCamera,

    /// <summary>The guided steps run.</summary>
    Running,
}

/// <summary>
/// The gate test's brain (Mac <c>AppModel+Gate</c> for the gate purpose): waits for the camera, drives a Core
/// <see cref="GateTestSession"/>, and turns the finished run into a <see cref="GateReport"/>. No timers or UI: the window
/// feeds it samples (<see cref="Ingest"/>), camera status (<see cref="OnStatus"/>) and time (<see cref="Tick"/>).
/// </summary>
public sealed class GateRun
{
    /// <summary>Seconds the camera gets to start.</summary>
    public const double CameraStartTimeout = 10;

    private readonly Func<double> _clock;
    private readonly Func<DateTimeOffset> _now;
    private GateTestSession? _session;
    private GateTestConfiguration _configuration = new();
    private DisplayLayout _layout = DisplayLayout.Empty;
    private string? _cameraName;
    private double _deadline;

    /// <summary>Creates a run over a monotonic clock (seconds) and a wall clock.</summary>
    public GateRun(Func<double> clock, Func<DateTimeOffset> now)
    {
        _clock = clock;
        _now = now;
    }

    /// <summary>What it is doing.</summary>
    public GateRunState State { get; private set; }

    /// <summary>The progress of the step in progress; null unless running.</summary>
    public GateProgress? Progress => State == GateRunState.Running ? _session?.Progress : null;

    /// <summary>The last message (a cancel or an error); null when there is none.</summary>
    public string? Message { get; private set; }

    /// <summary>True when <see cref="Message"/> is an error.</summary>
    public bool MessageIsError { get; private set; }

    /// <summary>The report of the last finished run.</summary>
    public GateReport? Report { get; private set; }

    /// <summary>True when the layout has enough displays.</summary>
    public static bool CanRun(DisplayLayout layout) => layout.Displays.Count >= GateTestSession.MinimumDisplays;

    /// <summary>
    /// Begins a run: checks the displays and waits for the camera. Returns false (and sets <see cref="Message"/>) when it cannot
    /// start. <paramref name="status"/> is the camera status now, so a camera that already runs begins at once.
    /// </summary>
    public bool Begin(DisplayLayout layout, string? cameraName, GateTestConfiguration configuration, CaptureStatus status)
    {
        if (State != GateRunState.Idle)
        {
            return false;
        }

        Message = null;
        MessageIsError = false;
        if (!CanRun(layout))
        {
            Fail(GateTestWording.NeedsTwoDisplaysOnStart);
            return false;
        }

        _layout = layout;
        _cameraName = cameraName;
        _configuration = configuration;
        Report = null;
        State = GateRunState.StartingCamera;
        _deadline = _clock() + CameraStartTimeout;
        OnStatus(status);
        return true;
    }

    /// <summary>Reacts to the camera status while a run starts or runs.</summary>
    public void OnStatus(CaptureStatus status)
    {
        switch (State)
        {
            case GateRunState.StartingCamera:
                if (status is CaptureStatus.Running)
                {
                    _session = new GateTestSession(_layout, _cameraName, _configuration, _clock);
                    _session.Start();
                    State = GateRunState.Running;
                    CompleteIfFinished();
                }
                else if (status is CaptureStatus.Failed failed)
                {
                    Abort($"The camera failed: {CaptureText.UserMessage(failed.Error)}.");
                }

                break;
            case GateRunState.Running:
                if (status is not CaptureStatus.Running)
                {
                    Abort(GateTestWording.CameraStopped(status));
                }

                break;
        }
    }

    /// <summary>Advances time: the camera timeout, the settle and hold phases, and the end of the run.</summary>
    public void Tick()
    {
        switch (State)
        {
            case GateRunState.StartingCamera:
                if (_clock() >= _deadline)
                {
                    Abort(GateTestWording.CameraTimeout);
                }

                break;
            case GateRunState.Running:
                _session?.Advance();
                CompleteIfFinished();
                break;
        }
    }

    /// <summary>Offers a sample that has just arrived; it counts only during a hold.</summary>
    public void Ingest(TimedPose sample)
    {
        if (State != GateRunState.Running || _session is null)
        {
            return;
        }

        _session.Ingest(sample);
        CompleteIfFinished();
    }

    /// <summary>Cancels a run (the button, Esc, closing the window). Does nothing when idle.</summary>
    public void Cancel(string? message = GateTestWording.Cancelled)
    {
        if (State == GateRunState.Idle)
        {
            return;
        }

        _session?.Cancel();
        End();
        Message = message;
        MessageIsError = false;
    }

    /// <summary>Returns to the setup form: forgets the report and the message.</summary>
    public void ShowSetup()
    {
        if (State == GateRunState.Idle)
        {
            Report = null;
            Message = null;
            MessageIsError = false;
        }
    }

    /// <summary>The trace of a finished session as labeled samples for the analysis (label = display id).</summary>
    public static List<PoseSample> Flatten(LabeledPoseTrace trace)
    {
        var samples = new List<PoseSample>();
        foreach (var segment in trace.Segments)
        {
            if (segment.DisplayId is not { } id)
            {
                continue;
            }

            foreach (var s in segment.Samples)
            {
                samples.Add(s.Pose is { } p
                    ? new PoseSample(s.Timestamp, p.Yaw, p.Pitch, p.Roll, id.RawValue, segment.Round)
                    : new PoseSample(s.Timestamp, 0, 0, 0, id.RawValue, segment.Round, false));
            }
        }

        return samples;
    }

    private void CompleteIfFinished()
    {
        if (_session is not { Phase: GateSessionPhase.Finished } session)
        {
            return;
        }

        var trace = session.MakeTrace(_now());
        End();
        if (trace is null)
        {
            return;
        }

        Report = new GateReport(SeparabilityAnalyzer.Analyze(Flatten(trace)), _layout, _cameraName, _configuration, _now());
    }

    private void Abort(string text)
    {
        _session?.Cancel();
        End();
        Fail(text);
    }

    /// <summary>Shows an error on the setup form (the camera could not be asked for, say).</summary>
    public void Fail(string text)
    {
        Message = text;
        MessageIsError = true;
    }

    private void End()
    {
        State = GateRunState.Idle;
        _session = null;
    }
}
