namespace Sway.Core.Runtime;

/// <summary>How much frame rate the app wants from the pose source.</summary>
public enum PoseDemand
{
    /// <summary>Every frame up to the 15 fps cap: calibration, the gate test, Setup's camera check, Pose Debug.</summary>
    Full,

    /// <summary>Tracking: full rate while a face is present; a lower rate once no face has been seen for 2 s.</summary>
    Adaptive,

    /// <summary>The camera must stay on, but nothing switches on the poses (Focus Lock): a low rate.</summary>
    Reduced,
}

/// <summary>
/// Decides which camera frames are worth running the pose estimator on (SPEC D45). The minimum interval between
/// processed frames depends on the demand, whether the machine is power constrained (battery saver or a thermal
/// limit), and whether a face has been seen in the last 2 s:
/// <code>
/// Demand                                  No power constraint   Power constrained
/// Full                                    1/15 s                1/15 s
/// Adaptive, face seen within 2 s          1/15 s                1/10 s
/// Adaptive, no face for 2 s or more       1/5 s                 1/2 s
/// Reduced                                 1/5 s                 1/2 s
/// </code>
/// No row exceeds 15 fps. The intervals are averages kept on a nominal schedule: the class keeps a slot, the time the
/// last processed frame was due. A frame is processed when <c>t &gt;= slot + interval - 0.004</c>, and the slot then
/// moves to <c>max(slot + interval, t - interval / 2)</c>. It has no clock of its own: it works on frame timestamps
/// in seconds that only need to be comparable with each other. Not thread safe.
/// </summary>
public sealed class AdaptiveFrameRate : IEquatable<AdaptiveFrameRate>
{
    /// <summary>The shortest interval ever used: the hard 15 fps cap.</summary>
    public const double FullInterval = 1.0 / 15;

    /// <summary>With Adaptive, a face present, and a power constraint.</summary>
    public const double ConstrainedActiveInterval = 1.0 / 10;

    /// <summary>With Reduced, or Adaptive and no face for <see cref="AbsenceThreshold"/>.</summary>
    public const double IdleInterval = 1.0 / 5;

    /// <summary>The same as <see cref="IdleInterval"/> when power constrained.</summary>
    public const double ConstrainedIdleInterval = 1.0 / 2;

    /// <summary>How long without a face before Adaptive drops to the idle interval, in seconds.</summary>
    public const double AbsenceThreshold = 2;

    /// <summary>Slack when comparing a frame gap with an interval, in seconds.</summary>
    public const double JitterTolerance = 0.004;

    private double? _slot;
    private double? _lastFaceSeen;

    /// <summary>How much frame rate the app wants now.</summary>
    public PoseDemand Demand { get; private set; } = PoseDemand.Full;

    /// <summary>Whether battery saver or a thermal limit applies.</summary>
    public bool IsPowerConstrained { get; private set; }

    /// <summary>The minimum time between two processed frames, for a frame at time <paramref name="t"/>.</summary>
    public double MinimumInterval(double t) => Demand switch
    {
        PoseDemand.Full => FullInterval,
        PoseDemand.Adaptive => HasSeenFace(AbsenceThreshold, t)
            ? (IsPowerConstrained ? ConstrainedActiveInterval : FullInterval)
            : (IsPowerConstrained ? ConstrainedIdleInterval : IdleInterval),
        _ => IsPowerConstrained ? ConstrainedIdleInterval : IdleInterval,
    };

    /// <summary>
    /// Whether the frame at time <paramref name="t"/> should be estimated. A true answer counts the frame as
    /// processed, so the caller must then estimate it and call <see cref="Record"/>. The first frame after a reset is
    /// always processed, and so is a frame earlier than the slot by more than the jitter tolerance (the clock
    /// restarted); a face-seen time from the future is pulled back to <paramref name="t"/> then.
    /// </summary>
    public bool ShouldProcess(double t)
    {
        if (_slot is not { } due)
        {
            _slot = t;
            _lastFaceSeen = t;
            return true;
        }

        if (t < due - JitterTolerance)
        {
            if (_lastFaceSeen is { } seen && seen > t)
            {
                _lastFaceSeen = t;
            }

            _slot = t;
            return true;
        }

        var interval = MinimumInterval(t);
        if (t < due + interval - JitterTolerance)
        {
            return false;
        }

        _slot = Math.Max(due + interval, t - (interval / 2));
        return true;
    }

    /// <summary>Records the outcome of estimating the frame at <paramref name="t"/>. A frame without a face changes nothing.</summary>
    public void Record(bool face, double t)
    {
        if (face)
        {
            _lastFaceSeen = t;
        }
    }

    /// <summary>Changes the demand. Takes effect from the next frame.</summary>
    public void SetDemand(PoseDemand demand) => Demand = demand;

    /// <summary>Records whether battery saver or a thermal limit applies. Takes effect on the next frame.</summary>
    public void SetPowerConstrained(bool isPowerConstrained) => IsPowerConstrained = isPowerConstrained;

    /// <summary>
    /// Starts a new run: the next frame is processed, a face counts as just seen, and the demand is Full. The power
    /// state is kept, since it describes the machine and not the run.
    /// </summary>
    public void Reset()
    {
        Demand = PoseDemand.Full;
        _slot = null;
        _lastFaceSeen = null;
    }

    public bool Equals(AdaptiveFrameRate? other) =>
        other is not null
        && Demand == other.Demand
        && IsPowerConstrained == other.IsPowerConstrained
        && _slot == other._slot
        && _lastFaceSeen == other._lastFaceSeen;

    public override bool Equals(object? obj) => Equals(obj as AdaptiveFrameRate);

    public override int GetHashCode() => HashCode.Combine(Demand, IsPowerConstrained, _slot, _lastFaceSeen);

    private bool HasSeenFace(double window, double t) => _lastFaceSeen is not { } seen || t - seen < window;
}

/// <summary>What the app is doing, as far as the frame rate is concerned.</summary>
public readonly record struct PoseDemandInputs(
    bool GateTestRunning = false,
    bool SetupUsingCamera = false,
    bool PoseDebugVisible = false,
    bool FocusTestVisible = false,
    bool FocusLocked = false,
    bool SwitchingUnavailable = false);

/// <summary>How the inputs combine into one <see cref="PoseDemand"/> (CONTRACT 6.5.5 item 2).</summary>
public static class PoseDemandPolicy
{
    /// <summary>
    /// Full for everything but plain tracking: the gate test, a calibration or Setup's camera check, the Pose Debug
    /// window, and the Focus Test window. Otherwise Reduced while nothing is switched on the poses (Focus Lock is on,
    /// or <c>SwitchingUnavailable</c>: Sway cannot focus windows right now). Otherwise Adaptive.
    /// </summary>
    public static PoseDemand Wanted(PoseDemandInputs inputs)
    {
        if (inputs.GateTestRunning || inputs.SetupUsingCamera || inputs.PoseDebugVisible || inputs.FocusTestVisible)
        {
            return PoseDemand.Full;
        }

        if (inputs.FocusLocked || inputs.SwitchingUnavailable)
        {
            return PoseDemand.Reduced;
        }

        return PoseDemand.Adaptive;
    }
}
