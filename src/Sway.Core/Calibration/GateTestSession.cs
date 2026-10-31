namespace Sway.Core.Calibration;

/// <summary>Where a <see cref="GateTestSession"/> is.</summary>
public enum GateSessionPhase
{
    /// <summary>Not started.</summary>
    Pending,

    /// <summary>The user is moving their gaze; samples are discarded.</summary>
    Settling,

    /// <summary>Samples are recorded.</summary>
    Holding,

    /// <summary>Every step ran.</summary>
    Finished,

    /// <summary>Aborted; nothing is kept.</summary>
    Cancelled,
}

/// <summary>One display visit.</summary>
/// <param name="Number">1-based index across the whole run.</param>
/// <param name="Round">1-based round.</param>
/// <param name="DisplayNumber">1-based position of the display in layout order.</param>
/// <param name="Display">The display to look at.</param>
public sealed record GateStep(int Number, int Round, int DisplayNumber, DisplayInfo Display);

/// <summary>
/// The pure state machine behind the guided run (Mac <c>GateTestSession</c>, CONTRACT 6.5, SPEC F7). Steps are rounds
/// x displays, round-major, displays in layout order (left to right). Each step has a settle phase (samples discarded)
/// followed by a hold phase (every sample recorded, including frames without a face). Time comes only from the
/// injected clock: a phase starts at the moment the machine first observes the previous phase's end, so the schedule
/// is driven by whichever of <see cref="Advance"/> or <see cref="Ingest"/> runs first, and a sample that arrives after
/// the hold deadline is never recorded into the finished segment. No I/O, no timers.
/// </summary>
public sealed class GateTestSession
{
    /// <summary>Fewer displays than this cannot be separated.</summary>
    public const int MinimumDisplays = 2;

    private readonly Func<double> _clock;
    private readonly double _settleDuration;
    private readonly List<TraceSegment> _segments = [];
    private List<TimedPose> _currentSamples = [];
    private int _stepIndex;
    private double _phaseStart;

    /// <summary>Creates a session over <paramref name="layout"/>.</summary>
    /// <param name="layout">The displays, in layout order.</param>
    /// <param name="cameraName">The camera that records it.</param>
    /// <param name="configuration">Rounds and hold time.</param>
    /// <param name="clock">A monotonic clock, seconds.</param>
    /// <param name="settleDuration">Seconds of settling per look.</param>
    public GateTestSession(
        DisplayLayout layout,
        string? cameraName,
        GateTestConfiguration configuration,
        Func<double> clock,
        double settleDuration = GateTestConfiguration.SettleDuration)
    {
        Layout = layout;
        CameraName = cameraName;
        Configuration = configuration;
        _clock = clock;
        _settleDuration = settleDuration;
        var steps = new List<GateStep>();
        for (var round = 1; round <= configuration.Rounds; round++)
        {
            for (var offset = 0; offset < layout.Displays.Count; offset++)
            {
                steps.Add(new GateStep(steps.Count + 1, round, offset + 1, layout.Displays[offset]));
            }
        }

        Steps = steps;
    }

    /// <summary>The displays the run covers.</summary>
    public DisplayLayout Layout { get; }

    /// <summary>The camera name recorded in the trace.</summary>
    public string? CameraName { get; }

    /// <summary>Rounds and hold time.</summary>
    public GateTestConfiguration Configuration { get; }

    /// <summary>Every step, round-major.</summary>
    public IReadOnlyList<GateStep> Steps { get; }

    /// <summary>The phase now.</summary>
    public GateSessionPhase Phase { get; private set; } = GateSessionPhase.Pending;

    /// <summary>True while the run waits for samples (settling or holding).</summary>
    public bool IsActive => Phase is GateSessionPhase.Settling or GateSessionPhase.Holding;

    /// <summary>The step in progress; null before the start and after the end.</summary>
    public GateStep? CurrentStep => IsActive ? Steps[_stepIndex] : null;

    /// <summary>Begins the first step's settle phase. A layout without displays finishes at once with no segments.</summary>
    public void Start()
    {
        if (Phase != GateSessionPhase.Pending)
        {
            return;
        }

        if (Steps.Count == 0)
        {
            Phase = GateSessionPhase.Finished;
            return;
        }

        _stepIndex = 0;
        Phase = GateSessionPhase.Settling;
        _phaseStart = _clock();
    }

    /// <summary>Applies any phase transitions that are due at the current clock reading.</summary>
    public void Advance()
    {
        if (!IsActive)
        {
            return;
        }

        var now = _clock();
        if (Phase == GateSessionPhase.Settling && now - _phaseStart >= _settleDuration)
        {
            Phase = GateSessionPhase.Holding;
            _phaseStart = now;
            _currentSamples = [];
        }

        if (Phase == GateSessionPhase.Holding && now - _phaseStart >= Configuration.HoldDuration)
        {
            CloseCurrentStep(now);
        }
    }

    /// <summary>Offers a sample that has just arrived. It is recorded only if the hold phase is open at the current clock reading.</summary>
    public void Ingest(TimedPose sample)
    {
        Advance();
        if (Phase == GateSessionPhase.Holding)
        {
            _currentSamples.Add(sample);
        }
    }

    /// <summary>Aborts the run and discards everything recorded. A finished run cannot be cancelled.</summary>
    public void Cancel()
    {
        if (Phase == GateSessionPhase.Finished)
        {
            return;
        }

        Phase = GateSessionPhase.Cancelled;
        _segments.Clear();
        _currentSamples = [];
    }

    /// <summary>A snapshot of the current phase for the UI; null unless settling or holding.</summary>
    public GateProgress? Progress
    {
        get
        {
            if (CurrentStep is not { } step)
            {
                return null;
            }

            var settling = Phase == GateSessionPhase.Settling;
            var duration = settling ? _settleDuration : Configuration.HoldDuration;
            var elapsed = Math.Min(Math.Max(0, _clock() - _phaseStart), duration);
            return new GateProgress(
                settling ? GatePhase.Settle : GatePhase.Hold,
                step.Number,
                Steps.Count,
                step.Round,
                Configuration.Rounds,
                step.DisplayNumber,
                Layout.Displays.Count,
                step.Display,
                elapsed,
                duration,
                settling ? 0 : _currentSamples.Count,
                settling ? 0 : _currentSamples.Count(s => s.Pose is not null));
        }
    }

    /// <summary>The finished run as a labeled trace; null unless it ran to completion.</summary>
    public LabeledPoseTrace? MakeTrace(DateTimeOffset createdAt) =>
        Phase == GateSessionPhase.Finished
            ? new LabeledPoseTrace { CreatedAt = createdAt, CameraName = CameraName, Layout = Layout, Segments = _segments.ToList() }
            : null;

    private void CloseCurrentStep(double now)
    {
        var step = Steps[_stepIndex];
        _segments.Add(new TraceSegment(step.Display.Id, step.Round, _currentSamples));
        _currentSamples = [];
        if (_stepIndex + 1 < Steps.Count)
        {
            _stepIndex++;
            Phase = GateSessionPhase.Settling;
            _phaseStart = now;
        }
        else
        {
            Phase = GateSessionPhase.Finished;
        }
    }
}
