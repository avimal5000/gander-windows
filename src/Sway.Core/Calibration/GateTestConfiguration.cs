namespace Sway.Core.Calibration;

/// <summary>
/// Settings for one guided run over the displays (SPEC F7, D30). Port of the Mac <c>GateTestConfiguration</c>. The
/// guided calibration is <see cref="Calibration"/>: two rounds, 1.5 s of settling and 2.5 s of hold per look.
/// </summary>
public sealed record GateTestConfiguration
{
    /// <summary>Fewest rounds allowed.</summary>
    public const int MinRounds = 1;

    /// <summary>Most rounds allowed.</summary>
    public const int MaxRounds = 3;

    /// <summary>Shortest hold the gate test window allows, in seconds.</summary>
    public const double MinHold = 3;

    /// <summary>Longest hold allowed, in seconds.</summary>
    public const double MaxHold = 10;

    /// <summary>Default rounds.</summary>
    public const int DefaultRounds = 2;

    /// <summary>Default hold, in seconds.</summary>
    public const double DefaultHoldDuration = 5;

    /// <summary>Time the user gets to move their gaze before samples count, in seconds.</summary>
    public const double SettleDuration = 1.5;

    /// <summary>The guided calibration's settings (SPEC D30). Its hold is shorter than the gate test window allows.</summary>
    public static readonly GateTestConfiguration Calibration = new(2, 2.5, 2.5);

    private readonly double _shortestHold;

    /// <summary>Creates a configuration; values are forced into the allowed ranges (hold at least 3 s).</summary>
    public GateTestConfiguration(int rounds = DefaultRounds, double holdDuration = DefaultHoldDuration)
        : this(rounds, holdDuration, MinHold)
    {
    }

    private GateTestConfiguration(int rounds, double holdDuration, double shortestHold)
    {
        _shortestHold = shortestHold;
        Rounds = Math.Min(Math.Max(rounds, MinRounds), MaxRounds);
        HoldDuration = Math.Min(Math.Max(holdDuration, shortestHold), MaxHold);
    }

    /// <summary>Passes through each display in layout order.</summary>
    public int Rounds { get; }

    /// <summary>Seconds of recorded samples per display and round.</summary>
    public double HoldDuration { get; }

    /// <summary>Rough total running time for <paramref name="displayCount"/> displays, settle phases included.</summary>
    public double EstimatedDuration(int displayCount) => Rounds * displayCount * (SettleDuration + HoldDuration);

    /// <inheritdoc />
    public bool Equals(GateTestConfiguration? other) =>
        other is not null && Rounds == other.Rounds && HoldDuration == other.HoldDuration && _shortestHold == other._shortestHold;

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Rounds, HoldDuration, _shortestHold);
}

/// <summary>A snapshot of a running guided run for the UI (Mac <c>GateProgress</c>).</summary>
/// <param name="Phase">Settling (samples discarded) or holding (recording).</param>
/// <param name="StepNumber">1-based index of the current step out of <paramref name="StepCount"/> (rounds x displays).</param>
/// <param name="StepCount">Rounds times displays.</param>
/// <param name="Round">1-based round.</param>
/// <param name="Rounds">Number of rounds.</param>
/// <param name="DisplayNumber">1-based position of the target in layout order.</param>
/// <param name="DisplayCount">Number of displays.</param>
/// <param name="Display">The display the user is asked to look at.</param>
/// <param name="PhaseElapsed">Seconds spent in the current phase, clamped to the phase duration.</param>
/// <param name="PhaseDuration">Length of the current phase, seconds.</param>
/// <param name="RecordedSamples">Samples recorded so far in the current hold (0 while settling).</param>
/// <param name="RecordedFaces">How many of those had a face.</param>
public sealed record GateProgress(
    GatePhase Phase,
    int StepNumber,
    int StepCount,
    int Round,
    int Rounds,
    int DisplayNumber,
    int DisplayCount,
    DisplayInfo Display,
    double PhaseElapsed,
    double PhaseDuration,
    int RecordedSamples,
    int RecordedFaces)
{
    /// <summary>Seconds left in the current phase.</summary>
    public double Remaining => Math.Max(0, PhaseDuration - PhaseElapsed);

    /// <summary>0 to 1 progress through the current phase.</summary>
    public double Fraction => PhaseDuration > 0 ? Math.Min(1, Math.Max(0, PhaseElapsed / PhaseDuration)) : 1;
}

/// <summary>The two phases of one look.</summary>
public enum GatePhase
{
    /// <summary>Counting down; samples are discarded.</summary>
    Settle,

    /// <summary>Recording samples for the target display.</summary>
    Hold,
}
