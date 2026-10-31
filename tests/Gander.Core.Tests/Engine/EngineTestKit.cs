namespace Gander.Core.Tests;

/// <summary>Builders shared by the engine tests (ports of PoseFactory, TraceFactory, HandProfile, DeciderHarness).</summary>
internal static class Poses
{
    /// <summary>A pose with the given angles. Vision angles are the default feature set; landmark angles are null unless given.</summary>
    public static HeadPose Make(double yaw, double pitch, double confidence = 0.95, double? landmarkYaw = null, double? landmarkPitch = null) =>
        new(yaw, pitch, 0, confidence, landmarkYaw, landmarkPitch);

    /// <summary>A sample at time t; a null yaw means no face.</summary>
    public static TimedPose Sample(double t, double? yaw, double pitch = 8, double confidence = 0.95) =>
        new(t, yaw is { } y ? Make(y, pitch, confidence) : null);

    /// <summary>Left-to-right layout of 1920x1080 displays named "Display n".</summary>
    public static DisplayLayout Layout(params DisplayId[] ids) =>
        new(ids.Select((id, index) => new DisplayInfo(
            id, (uint)(index + 1), $"Display {index + 1}", new DisplayRect(index * 1920.0, 0, 1920, 1080), index == 0, index == 0)));
}

/// <summary>A half-open range of sample indices.</summary>
internal readonly record struct IndexRange(int Lower, int Upper)
{
    public int Count => Upper - Lower;

    public bool Contains(int index) => index >= Lower && index < Upper;
}

/// <summary>Builds small labeled traces with exactly known statistics for the fitter tests.</summary>
internal static class TraceFactory
{
    public static readonly DateTimeOffset CreatedAt = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);

    /// <summary>
    /// A segment of count samples whose angles alternate center + spread, center - spread, and so on. With an even
    /// count the mean is exactly the center and the sample standard deviation is spread * sqrt(n / (n - 1)).
    /// </summary>
    public static TraceSegment Segment(
        DisplayId? display, int count, double yaw, double pitch, int round = 1, double yawSpread = 1, double pitchSpread = 1, double startTime = 0)
    {
        var samples = Enumerable.Range(0, count).Select(index =>
        {
            var sign = index % 2 == 0 ? 1.0 : -1.0;
            var pose = Poses.Make(
                yaw + (sign * yawSpread),
                pitch + (sign * pitchSpread),
                landmarkYaw: (yaw * 0.9) + (sign * yawSpread),
                landmarkPitch: (pitch * 0.9) + (sign * pitchSpread));
            return new TimedPose(startTime + ((double)index / 15), pose);
        }).ToList();
        return new TraceSegment(display, round, samples);
    }

    /// <summary>A segment of count samples that all have no face.</summary>
    public static TraceSegment EmptySegment(DisplayId? display, int count, int round = 1) =>
        new(display, round, Enumerable.Range(0, count).Select(i => new TimedPose((double)i / 15, null)).ToList());

    public static LabeledPoseTrace Trace(IEnumerable<TraceSegment> segments, DisplayLayout? layout = null, string? cameraName = "Test camera") =>
        new() { CreatedAt = CreatedAt, CameraName = cameraName, Layout = layout, Segments = segments.ToList() };
}

/// <summary>
/// A small hand-made profile whose numbers are easy to work with in the state machine tests. Three displays on one
/// pitch line (pitch 8), yaw 60 / 40 / 0, yawScale 2, pitchScale 4. A pose at yaw 50 + 10m is nearest A with margin m.
/// </summary>
internal static class HandProfile
{
    public static readonly DisplayId A = "A";
    public static readonly DisplayId B = "B";
    public static readonly DisplayId C = "C";

    public static readonly CalibrationProfile Profile = new(
        PoseFeatureSet.VisionAngles,
        [
            new DisplayProfile(A, 60, 8, 1, 1, 100),
            new DisplayProfile(B, 40, 8, 1, 1, 100),
            new DisplayProfile(C, 0, 8, 1, 1, 100),
        ],
        2,
        4,
        "hand",
        null,
        DateTimeOffset.UnixEpoch);

    public const double YawA = 60.0;
    public const double YawB = 40.0;
    public const double YawC = 0.0;

    /// <summary>Nearest A with margin 0.2: between the stay margin (0.15) and the enter margin (0.30).</summary>
    public const double YawWeakA = 52.0;

    /// <summary>Nearest A with margin 0.1: below the stay margin.</summary>
    public const double YawVeryWeakA = 51.0;

    /// <summary>Far outside the elsewhere box.</summary>
    public const double YawElsewhere = 100.0;

    /// <summary>Defaults with a binary-exact dwell (0.5 s), so timestamps on a 0.125 s grid compare exactly.</summary>
    public static readonly SwitchSettings Settings = SwitchSettings.Default with { Dwell = 0.5 };
}

/// <summary>Feeds samples to a state machine with a mutable context, for readable rule tests.</summary>
internal sealed class DeciderHarness
{
    public DeciderHarness(SwitchSettings? settings = null, DisplayId? focused = null, bool focusedGiven = false)
    {
        Machine = new SwitchStateMachine(HandProfile.Profile, settings ?? HandProfile.Settings);
        Context = new SwitchContext(focusedGiven ? focused : HandProfile.B);
    }

    public SwitchStateMachine Machine { get; }

    public SwitchContext Context { get; set; }

    /// <summary>A harness whose focused display is unknown.</summary>
    public static DeciderHarness WithoutFocus() => new(focused: null, focusedGiven: true);

    /// <summary>Processes one sample at time t; a null yaw means no face.</summary>
    public SwitchDecision Feed(double t, double? yaw, double confidence = 0.95) =>
        Machine.Process(Poses.Sample(t, yaw, confidence: confidence), Context);

    /// <summary>Processes a steady stream of samples with the same yaw, start through end inclusive. Never skips a frame.</summary>
    public List<SwitchDecision> Run(double start, double end, double? yaw, double step = 0.125)
    {
        var decisions = new List<SwitchDecision>();
        for (var i = 0; start + (i * step) <= end; i++)
        {
            decisions.Add(Feed(start + (i * step), yaw));
        }

        return decisions;
    }
}

/// <summary>
/// Deterministic pseudo-random generator (SplitMix64) with Gaussian sampling (Box-Muller), so synthetic scenarios
/// replay identically on every run. Test-target copy; the engine itself uses no randomness.
/// </summary>
internal sealed class SplitMix64
{
    private ulong _state;
    private double? _spareGaussian;

    public SplitMix64(ulong seed)
    {
        _state = seed;
    }

    public ulong Next()
    {
        unchecked
        {
            _state += 0x9E3779B97F4A7C15UL;
            var z = _state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }

    /// <summary>Uniform sample in [0, 1).</summary>
    public double NextUnit() => (double)(Next() >> 11) * (1.0 / 9007199254740992.0);

    /// <summary>Standard normal sample.</summary>
    public double NextGaussian()
    {
        if (_spareGaussian is { } spare)
        {
            _spareGaussian = null;
            return spare;
        }

        var u1 = 1.0 - NextUnit();
        var u2 = NextUnit();
        var radius = Math.Sqrt(-2.0 * Math.Log(u1));
        _spareGaussian = radius * Math.Sin(2.0 * Math.PI * u2);
        return radius * Math.Cos(2.0 * Math.PI * u2);
    }
}

/// <summary>The three-display set-up of the synthetic scenarios: display yaws 61 / 42 / 2 and pitches 5 / 10 / 8 degrees.</summary>
internal static class ScenarioWorld
{
    public static readonly DisplayId[] Ids = ["scenario-left", "scenario-middle", "scenario-right"];
    public static readonly double[] Yaws = [61.0, 42.0, 2.0];
    public static readonly double[] Pitches = [5.0, 10.0, 8.0];
    public const double YawStd = 1.5;
    public const double PitchStd = 2.5;
    public const double FrameRate = 15.0;

    public static DisplayId Left => Ids[0];

    public static DisplayId Middle => Ids[1];

    public static DisplayId Right => Ids[2];

    /// <summary>Where the head points when looking at the center of the display.</summary>
    public static PoseScript.Target TargetOf(DisplayId display)
    {
        var index = Array.IndexOf(Ids, display);
        return index < 0 ? new PoseScript.Target(0, 0) : new PoseScript.Target(Yaws[index], Pitches[index]);
    }

    /// <summary>A labeled two-round gate trace of 75 samples per display and round, like the real gate test.</summary>
    public static LabeledPoseTrace CalibrationTrace(ulong seed)
    {
        var rng = new SplitMix64(seed);
        var segments = new List<TraceSegment>();
        var frame = 0;
        for (var round = 1; round <= 2; round++)
        {
            for (var index = 0; index < Ids.Length; index++)
            {
                var samples = new List<TimedPose>();
                for (var i = 0; i < 75; i++)
                {
                    var yaw = Yaws[index] + (rng.NextGaussian() * YawStd);
                    var pitch = Pitches[index] + (rng.NextGaussian() * PitchStd);
                    samples.Add(new TimedPose(frame / FrameRate, Poses.Make(yaw, pitch)));
                    frame++;
                }

                segments.Add(new TraceSegment(Ids[index], round, samples));
            }
        }

        return new LabeledPoseTrace
        {
            CreatedAt = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000),
            CameraName = "Synthetic",
            Layout = Poses.Layout(Ids),
            Segments = segments,
        };
    }

    /// <summary>The profile fitted, with the real engine, from a synthetic labeled trace.</summary>
    public static CalibrationProfile Profile(ulong seed = 2026) =>
        new PoseSwitchEngine().FitProfile(CalibrationTrace(seed), PoseFeatureSet.VisionAngles, null);
}

/// <summary>Builds a pose stream at 15 fps from holds and linear turns, with seeded Gaussian noise.</summary>
internal sealed class PoseScript
{
    private readonly List<TimedPose> _samples = [];
    private readonly SplitMix64 _rng;
    private bool _lastFrameWasDrop;

    public PoseScript(Target start, ulong seed)
    {
        Current = start;
        _rng = new SplitMix64(seed);
    }

    public readonly record struct Target(double Yaw, double Pitch);

    public IReadOnlyList<TimedPose> Samples => _samples;

    public Target Current { get; private set; }

    /// <summary>Number of frames covering the seconds.</summary>
    public static int Frames(double seconds) => (int)Math.Round(seconds * ScenarioWorld.FrameRate, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Stays at the target. dropRate is the chance that a frame loses its face; two drops never follow each other.
    /// Returns the indices of the samples added.
    /// </summary>
    public IndexRange Hold(Target target, double seconds, double noise = 1, double dropRate = 0)
    {
        var first = _samples.Count;
        for (var i = 0; i < Frames(seconds); i++)
        {
            Append(target, noise, dropRate);
        }

        Current = target;
        return new IndexRange(first, _samples.Count);
    }

    /// <summary>Turns linearly from the current pose to the target over the seconds, ending exactly on it.</summary>
    public IndexRange Turn(Target target, double seconds, double noise = 1)
    {
        var first = _samples.Count;
        var start = Current;
        var frames = Math.Max(1, Frames(seconds));
        for (var step = 1; step <= frames; step++)
        {
            var fraction = (double)step / frames;
            Append(new Target(start.Yaw + ((target.Yaw - start.Yaw) * fraction), start.Pitch + ((target.Pitch - start.Pitch) * fraction)), noise, 0);
        }

        Current = target;
        return new IndexRange(first, _samples.Count);
    }

    private void Append(Target point, double noise, double dropRate)
    {
        var timestamp = _samples.Count / ScenarioWorld.FrameRate;
        var drop = !_lastFrameWasDrop && dropRate > 0 && _rng.NextUnit() < dropRate;
        _lastFrameWasDrop = drop;
        if (drop)
        {
            _samples.Add(new TimedPose(timestamp, null));
            return;
        }

        var yaw = point.Yaw + (_rng.NextGaussian() * noise * ScenarioWorld.YawStd);
        var pitch = point.Pitch + (_rng.NextGaussian() * noise * ScenarioWorld.PitchStd);
        _samples.Add(new TimedPose(timestamp, Poses.Make(yaw, pitch)));
    }
}

/// <summary>The outcome of replaying a pose stream through a decider.</summary>
internal sealed class ScenarioRun
{
    public required List<SwitchDecision> Decisions { get; init; }

    public required List<SwitchEvent> Switches { get; init; }
}

/// <summary>A fired switch: the index of the sample that completed the dwell, its time, and the display.</summary>
internal readonly record struct SwitchEvent(int Index, double Time, DisplayId Display);

/// <summary>Replays samples through a decider made by the real engine, with perfect focus.</summary>
internal static class ScenarioRunner
{
    public static ScenarioRun Run(
        IReadOnlyList<TimedPose> samples, CalibrationProfile profile, DisplayId? initialFocus, SwitchSettings? settings = null, IReadOnlySet<DisplayId>? targets = null)
    {
        var decider = new PoseSwitchEngine().MakeDecider(profile, settings ?? SwitchSettings.Default);
        var focused = initialFocus;
        var decisions = new List<SwitchDecision>();
        var switches = new List<SwitchEvent>();
        for (var index = 0; index < samples.Count; index++)
        {
            var decision = decider.Process(samples[index], new SwitchContext(focused, false, targets));
            decisions.Add(decision);
            if (decision.SwitchTo is { } target)
            {
                switches.Add(new SwitchEvent(index, samples[index].Timestamp, target));
                focused = target;
            }
        }

        return new ScenarioRun { Decisions = decisions, Switches = switches };
    }
}
