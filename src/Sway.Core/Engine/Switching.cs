using System.Text.Json.Serialization;

namespace Sway.Core;

/// <summary>Which 2-D pose features the engine uses.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PoseFeatureSet>))]
public enum PoseFeatureSet
{
    /// <summary>(Yaw, Pitch) from the pose source. Live tracking uses this one.</summary>
    VisionAngles,

    /// <summary>(LandmarkYaw, LandmarkPitch) from landmark geometry.</summary>
    LandmarkAngles,
}

/// <summary>A pose reduced to the two numbers a feature set classifies on: X is the yaw-like axis, Y the pitch-like axis.</summary>
public readonly record struct FeaturePoint(double X, double Y);

/// <summary>Reads a <see cref="FeaturePoint"/> out of a pose.</summary>
public static class PoseFeatureSetExtensions
{
    /// <summary>
    /// The point the pose has in this feature set, or null when the pose lacks the features. VisionAngles always
    /// succeeds. LandmarkAngles needs both landmark angles; live tracking leaves them null, so a landmark profile
    /// classifies such poses as no face.
    /// </summary>
    public static FeaturePoint? PointOf(this PoseFeatureSet features, HeadPose pose) => features switch
    {
        PoseFeatureSet.VisionAngles => new FeaturePoint(pose.Yaw, pose.Pitch),
        _ => pose.LandmarkYaw is { } yaw && pose.LandmarkPitch is { } pitch ? new FeaturePoint(yaw, pitch) : null,
    };
}

/// <summary>One processed camera frame: a timestamp plus the head pose, if a face was found. (Mac name: PoseSample.)</summary>
/// <param name="Timestamp">Seconds on a monotonic clock. Only differences are meaningful.</param>
/// <param name="Pose">Null when no face was detected in the frame.</param>
public sealed record TimedPose(double Timestamp, HeadPose? Pose);

/// <summary>A contiguous run of samples, optionally labeled with the display the user was asked to look at.</summary>
/// <param name="DisplayId">The display the user was looking at; null for unlabeled recording.</param>
/// <param name="Round">1-based round within a gate test; 0 for free recordings.</param>
/// <param name="Samples">Samples in time order, including frames with no face.</param>
public sealed record TraceSegment(DisplayId? DisplayId, int Round, IReadOnlyList<TimedPose> Samples);

/// <summary>A recorded, labeled pose trace for fitting a profile. Numeric only: no images, ever. (Mac name: PoseTrace.)</summary>
public sealed record LabeledPoseTrace
{
    /// <summary>The format version written today.</summary>
    public const int CurrentFormatVersion = 1;

    /// <summary>When it was recorded.</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UnixEpoch;

    /// <summary>Name of the camera used, if any.</summary>
    public string? CameraName { get; init; }

    /// <summary>Display layout at recording time.</summary>
    public DisplayLayout? Layout { get; init; }

    /// <summary>The labeled segments.</summary>
    public IReadOnlyList<TraceSegment> Segments { get; init; } = [];

    /// <summary>Free text.</summary>
    public string? Notes { get; init; }
}

/// <summary>Tunables for the switch decision. Mirrors the Mac <c>SwitchSettings</c>.</summary>
public sealed record SwitchSettings
{
    /// <summary>The defaults: dwell 0.40 s, enter 0.30, stay 0.15, max gap 0.25 s, cooldown 0.75 s.</summary>
    public static readonly SwitchSettings Default = new();

    /// <summary>Seconds a new display must be held before focus switches.</summary>
    public double Dwell { get; init; } = 0.40;

    /// <summary>Margin a pose needs to start dwelling on a display.</summary>
    public double EnterMargin { get; init; } = 0.30;

    /// <summary>Lower margin that keeps an ongoing dwell alive (hysteresis band).</summary>
    public double StayMargin { get; init; } = 0.15;

    /// <summary>Longest run of no-face, elsewhere, or uncertain samples, in seconds, that a dwell survives.</summary>
    public double MaxGap { get; init; } = 0.25;

    /// <summary>Seconds after a switch during which no new dwell starts.</summary>
    public double Cooldown { get; init; } = 0.75;

    /// <summary>Faces below this confidence count as no face.</summary>
    public double MinFaceConfidence { get; init; } = 0.5;

    /// <summary>Degrees beyond the outermost calibrated yaw (either side) after which a pose is elsewhere.</summary>
    public double ElsewhereYawMargin { get; init; } = 20;

    /// <summary>Degrees beyond the outermost calibrated pitch (either side) after which a pose is elsewhere.</summary>
    public double ElsewherePitchMargin { get; init; } = 15;
}

/// <summary>Why a profile could not be fitted.</summary>
public enum CalibrationFailure
{
    /// <summary>Fewer than two displays have enough face samples to profile.</summary>
    TooFewDisplays,

    /// <summary>No sample in the trace has the requested features.</summary>
    FeaturesMissing,
}

/// <summary>Thrown by <see cref="ProfileFitter"/> (Mac: CalibrationError).</summary>
public sealed class CalibrationException : Exception
{
    /// <summary>Creates the exception.</summary>
    public CalibrationException(CalibrationFailure failure, PoseFeatureSet? features = null)
        : base(failure == CalibrationFailure.TooFewDisplays
            ? "Fewer than two displays have enough face samples."
            : $"No sample has the {features} features.")
    {
        Failure = failure;
        Features = features;
    }

    /// <summary>What went wrong.</summary>
    public CalibrationFailure Failure { get; }

    /// <summary>The missing feature set, for <see cref="CalibrationFailure.FeaturesMissing"/>.</summary>
    public PoseFeatureSet? Features { get; }
}

/// <summary>Where one pose points, relative to a calibration profile.</summary>
public sealed class PoseClassification : IEquatable<PoseClassification>
{
    /// <summary>What kind of result this is.</summary>
    public enum ClassKind
    {
        /// <summary>No usable face: no pose, confidence below the minimum, or the profile's features are missing.</summary>
        NoFace,

        /// <summary>Outside the region around the calibrated displays (keyboard, phone, looking away).</summary>
        Elsewhere,

        /// <summary>Nearest <see cref="Best"/>.</summary>
        Display,
    }

    /// <summary>The no-face result.</summary>
    public static readonly PoseClassification NoFace = new(ClassKind.NoFace, null, null, 0, new Dictionary<DisplayId, double>());

    /// <summary>Creates a classification.</summary>
    public PoseClassification(ClassKind kind, DisplayId? best, DisplayId? runnerUp, double margin, IReadOnlyDictionary<DisplayId, double> distances)
    {
        Kind = kind;
        Best = best;
        RunnerUp = runnerUp;
        Margin = margin;
        Distances = distances;
    }

    /// <summary>The kind.</summary>
    public ClassKind Kind { get; }

    /// <summary>Nearest calibrated display; null only for NoFace.</summary>
    public DisplayId? Best { get; }

    /// <summary>Second nearest; null for NoFace or a single display.</summary>
    public DisplayId? RunnerUp { get; }

    /// <summary>(dRunnerUp - dBest) / (dRunnerUp + dBest): 0 on the boundary, 1 at best's center. 0 unless Kind is Display.</summary>
    public double Margin { get; }

    /// <summary>Normalized distance (axis-scale units) to every calibrated display; empty for NoFace.</summary>
    public IReadOnlyDictionary<DisplayId, double> Distances { get; }

    /// <inheritdoc />
    public bool Equals(PoseClassification? other) =>
        other is not null && Kind == other.Kind && Best == other.Best && RunnerUp == other.RunnerUp
        && Margin.Equals(other.Margin) && Distances.Count == other.Distances.Count
        && Distances.All(pair => other.Distances.TryGetValue(pair.Key, out var value) && value.Equals(pair.Value));

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as PoseClassification);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Kind, Best, RunnerUp, Margin);
}

/// <summary>Facts from outside the pose stream that the decider needs, passed with every sample.</summary>
public sealed record SwitchContext
{
    /// <summary>Creates a context.</summary>
    public SwitchContext(DisplayId? focusedDisplay, bool isFocusLocked = false, IReadOnlySet<DisplayId>? targets = null)
    {
        FocusedDisplay = focusedDisplay;
        IsFocusLocked = isFocusLocked;
        Targets = targets;
    }

    /// <summary>Display whose window has keyboard focus now; null if unknown (any display may then be switched to).</summary>
    public DisplayId? FocusedDisplay { get; init; }

    /// <summary>Focus Lock or pause is on: never switch.</summary>
    public bool IsFocusLocked { get; init; }

    /// <summary>Displays focus may move to; null means every calibrated display.</summary>
    public IReadOnlySet<DisplayId>? Targets { get; init; }
}

/// <summary>The decider's state after a sample.</summary>
public abstract record SwitchPhase
{
    /// <summary>Nothing in progress.</summary>
    public static readonly SwitchPhase Idle = new IdlePhase();

    /// <summary>The Focus Lock phase.</summary>
    public static readonly SwitchPhase Locked = new LockedPhase();

    /// <summary>Holding on a display; progress is the elapsed fraction of the dwell, 0 to 1.</summary>
    public static SwitchPhase Dwelling(DisplayId display, double progress) => new DwellingPhase(display, progress);

    /// <summary>A switch fired; no new dwell starts before the sample timestamp until.</summary>
    public static SwitchPhase CoolingDown(double until) => new CoolingDownPhase(until);
}

/// <summary>No dwell or cooldown.</summary>
public sealed record IdlePhase : SwitchPhase;

/// <summary>Holding a look on <paramref name="Display"/>.</summary>
public sealed record DwellingPhase(DisplayId Display, double Progress) : SwitchPhase;

/// <summary>A switch fired; no new dwell starts before <paramref name="Until"/>.</summary>
public sealed record CoolingDownPhase(double Until) : SwitchPhase;

/// <summary>Focus Lock is on.</summary>
public sealed record LockedPhase : SwitchPhase;

/// <summary>Why a sample did not fire a switch.</summary>
public enum SwitchHoldReason
{
    /// <summary>No usable face.</summary>
    NoFace,

    /// <summary>Outside the elsewhere box.</summary>
    Elsewhere,

    /// <summary>Nearest display's margin is below the threshold that applies.</summary>
    Uncertain,

    /// <summary>The pose points at the display that already has focus.</summary>
    AlreadyFocused,

    /// <summary>Nearest display is outside the context's targets.</summary>
    NotATarget,

    /// <summary>Dwell in progress, not yet complete.</summary>
    Dwelling,

    /// <summary>A recent switch's cooldown is running.</summary>
    CoolingDown,

    /// <summary>Focus Lock is on.</summary>
    FocusLocked,
}

/// <summary>The decider's output for one sample.</summary>
/// <param name="SwitchTo">Set exactly on the sample that completes a dwell: move focus to this display now.</param>
/// <param name="Phase">The state after the sample.</param>
/// <param name="Reason">Why nothing fired; null when SwitchTo is set.</param>
/// <param name="Classification">The sample's classification.</param>
public sealed record SwitchDecision(DisplayId? SwitchTo, SwitchPhase Phase, SwitchHoldReason? Reason, PoseClassification Classification);

/// <summary>
/// A stateful switch decider bound to one profile and settings. Pure: no clock, no I/O; time comes only from sample
/// timestamps, so recorded traces replay deterministically.
/// </summary>
public interface ISwitchDecider
{
    /// <summary>Classifies the sample and advances the state machine.</summary>
    SwitchDecision Process(TimedPose sample, SwitchContext context);

    /// <summary>Drops any dwell or cooldown, as after a restart or a layout change.</summary>
    void Reset();
}

/// <summary>Fits calibration profiles, classifies poses, and creates deciders. Implemented by <see cref="PoseSwitchEngine"/>.</summary>
public interface ISwitchEngineMaking
{
    /// <summary>Fits a profile from a labeled trace. Only segments from <paramref name="rounds"/>, or all when null.</summary>
    /// <exception cref="CalibrationException">Too few displays, or the features are missing.</exception>
    CalibrationProfile FitProfile(LabeledPoseTrace trace, PoseFeatureSet features, IReadOnlySet<int>? rounds);

    /// <summary>Classifies one pose (null means no face) against a profile.</summary>
    PoseClassification Classify(HeadPose? pose, CalibrationProfile profile, SwitchSettings settings);

    /// <summary>A fresh decider in the idle phase.</summary>
    ISwitchDecider MakeDecider(CalibrationProfile profile, SwitchSettings settings);
}

/// <summary>App-wide constants. Change behavior here, not with literals scattered across modules.</summary>
public static class SwayPolicy
{
    /// <summary>Most displays tracking switches between (SPEC D22).</summary>
    public const int MaxTrackedDisplays = 3;

    /// <summary>Default analysis frame rate.</summary>
    public const double DefaultFrameRate = 15;
}
