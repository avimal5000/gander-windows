namespace Gander.Core.Tests;

/// <summary>
/// Port of the Mac AdaptiveRateScenarioTests: the adaptive frame rate seen from the switch decider. The poses the decider
/// receives are the ones that survive the thinning, so these scenarios thin a 15 fps stream the way the live source does
/// and replay the rest through the real engine. <see cref="AdaptiveRateModel"/> restates the contract's rule for
/// adaptive demand independently of any production type. The properties checked:
/// while a face has been seen within 2 s nothing is thinned; after an absence the first face is picked up within one
/// throttled interval and the switch fires within dwell plus one sample interval of the first qualifying sample; power
/// constrained (10 fps) a single lost sample does not cancel a dwell.
/// </summary>
public class AdaptiveRateScenarioTests
{
    private static readonly ulong[] Seeds = [11, 22, 33];
    private static readonly SwitchSettings Settings = SwitchSettings.Default;
    private const double Frame = 1 / ScenarioWorld.FrameRate;

    /// <summary>Timestamps are large in the app (host uptime), so the scenarios are shifted to a realistic value.</summary>
    private const double Uptime = 300_000.0;

    public static IEnumerable<object[]> AwayCases() =>
        new[] { false, true }.SelectMany(constrained =>
            Enumerable.Range(150, 8).SelectMany(frames => Seeds.Select(seed => new object[] { constrained, frames, seed })));

    public static IEnumerable<object[]> ConstrainedBySeed() =>
        new[] { false, true }.SelectMany(c => Seeds.Select(s => new object[] { c, s }));

    public static IEnumerable<object[]> SeedList() => Seeds.Select(s => new object[] { s });

    /// <summary>
    /// The contract's thinning of a stream under adaptive demand, on a device that delivers every frame given. The
    /// intervals are 1/15 s (1/10 s power constrained) while a face was seen within 2 s and 1/5 s (1/2 s) otherwise,
    /// kept on a nominal schedule: a frame is processed when t >= slot + interval - 0.004, and the slot then becomes
    /// max(slot + interval, t - interval / 2). The first frame sets the slot and counts as a face just seen.
    /// </summary>
    private sealed class AdaptiveRateModel(bool powerConstrained)
    {
        public List<TimedPose> Thin(IReadOnlyList<TimedPose> frames)
        {
            var kept = new List<TimedPose>();
            double? slot = null;
            var lastFace = frames.Count > 0 ? frames[0].Timestamp : 0;
            foreach (var sample in frames)
            {
                var t = sample.Timestamp;
                if (slot is { } due)
                {
                    var faceRecent = t - lastFace < 2;
                    var interval = faceRecent
                        ? (powerConstrained ? 1.0 / 10 : 1.0 / 15)
                        : (powerConstrained ? 1.0 / 2 : 1.0 / 5);
                    if (t < due + interval - 0.004)
                    {
                        continue;
                    }

                    slot = Math.Max(due + interval, t - (interval / 2));
                }
                else
                {
                    slot = t;
                }

                kept.Add(sample);
                if (sample.Pose is not null)
                {
                    lastFace = t;
                }
            }

            return kept;
        }
    }

    private static List<TimedPose> Shifted(IEnumerable<TimedPose> samples) =>
        samples.Select(s => new TimedPose(s.Timestamp + Uptime, s.Pose)).ToList();

    /// <summary>Replaces the poses at the indices by "no face", as if the person had left.</summary>
    private static List<TimedPose> Blanked(IReadOnlyList<TimedPose> samples, IndexRange indices)
    {
        var result = samples.ToList();
        for (var index = indices.Lower; index < indices.Upper; index++)
        {
            result[index] = result[index] with { Pose = null };
        }

        return result;
    }

    /// <summary>The index of the first sample at or after from that points at the target with at least the enter margin.</summary>
    private static int? FirstQualifying(IReadOnlyList<TimedPose> samples, ScenarioRun run, DisplayId target, int from = 0) =>
        Enumerable.Range(0, samples.Count).Select(i => (int?)i).FirstOrDefault(index =>
        {
            var classification = run.Decisions[index!.Value].Classification;
            return index >= from && classification.Kind == PoseClassification.ClassKind.Display && classification.Best == target
                && classification.Margin >= Settings.EnterMargin;
        });

    // A face present: nothing is thinned

    [Fact(DisplayName = "10 looks with single-frame drops and 1.5 s off-camera pauses: no frame is thinned, switching is unchanged")]
    public void SwitchingWithAFaceIsNotSlowedDown()
    {
        foreach (var seed in Seeds)
        {
            var profile = ScenarioWorld.Profile(seed);
            DisplayId[] displays = [ScenarioWorld.Left, ScenarioWorld.Right];
            var script = new PoseScript(ScenarioWorld.TargetOf(displays[1]), seed + 21);
            script.Hold(ScenarioWorld.TargetOf(displays[1]), 1);
            var looks = new List<(DisplayId Target, IndexRange Window)>();
            var pauses = new List<IndexRange>();
            for (var look = 0; look < 10; look++)
            {
                var target = displays[look % 2];
                var turn = script.Turn(ScenarioWorld.TargetOf(target), 0.3);
                var hold = script.Hold(ScenarioWorld.TargetOf(target), 2, dropRate: 0.02);
                looks.Add((target, new IndexRange(turn.Lower, hold.Upper)));
                if (look % 2 == 1)
                {
                    // The face is lost for 1.5 s, less than the 2 s after which the rate would drop.
                    pauses.Add(script.Hold(ScenarioWorld.TargetOf(target), 1.5));
                }
            }

            var blanked = pauses.Aggregate(script.Samples, (acc, pause) => Blanked(acc, pause));
            var frames = Shifted(blanked);

            // Every frame survives: a face was always seen within the last 2 s.
            var thinned = new AdaptiveRateModel(false).Thin(frames);
            Assert.True(thinned.SequenceEqual(frames));

            var thinnedRun = ScenarioRunner.Run(thinned, profile, displays[1]);
            var plainRun = ScenarioRunner.Run(frames, profile, displays[1]);
            Assert.Equal(plainRun.Switches, thinnedRun.Switches);
            Assert.Equal(10, thinnedRun.Switches.Count);

            foreach (var look in looks)
            {
                var fired = thinnedRun.Switches.First(s => look.Window.Contains(s.Index));
                Assert.Equal(look.Target, fired.Display);
                var start = FirstQualifying(frames, thinnedRun, look.Target, look.Window.Lower);
                Assert.NotNull(start);
                var latency = fired.Time - frames[start.Value].Timestamp;
                Assert.True(latency >= Settings.Dwell - SwitchStateMachine.TimeTolerance);
                Assert.True(latency <= Settings.Dwell + Frame + SwitchStateMachine.TimeTolerance);
            }
        }
    }

    // Away, then back

    [Theory(DisplayName = "A switch after 10 s away fires within dwell of the first qualifying sample and 1 s + dwell of the return")]
    [MemberData(nameof(AwayCases))]
    public void SwitchingAfterAnAbsence(bool powerConstrained, int awayFrames, ulong seed)
    {
        var profile = ScenarioWorld.Profile(seed);
        var origin = ScenarioWorld.Left;
        var target = ScenarioWorld.Right;
        var script = new PoseScript(ScenarioWorld.TargetOf(origin), seed + 22);
        script.Hold(ScenarioWorld.TargetOf(origin), 2);
        // The person leaves (no face) and comes back looking at the other display.
        var away = script.Hold(ScenarioWorld.TargetOf(target), awayFrames / 15.0);
        script.Hold(ScenarioWorld.TargetOf(target), 3);
        var frames = Shifted(Blanked(script.Samples, away));
        var returnTime = frames[away.Upper].Timestamp;

        var thinned = new AdaptiveRateModel(powerConstrained).Thin(frames);
        var run = ScenarioRunner.Run(thinned, profile, origin);

        // The absence really was throttled: sparse samples after the first 2 s (and at least one per interval).
        var awayTimes = thinned.Select(s => s.Timestamp)
            .Where(t => t >= frames[away.Lower].Timestamp + 2.6 && t < returnTime).ToList();
        var interval = powerConstrained ? 0.5 : 0.2;
        Assert.True(awayTimes.Count > 5);
        foreach (var (earlier, later) in awayTimes.Zip(awayTimes.Skip(1)))
        {
            // The schedule averages the interval on whole frames, so a gap is within one frame of it (0.5 s alternates
            // 7 and 8 frames).
            Assert.True(later - earlier >= interval - Frame + 1e-6);
            Assert.True(later - earlier <= interval + Frame + 1e-6);
        }

        Assert.True(thinned.Count < frames.Count - 40);

        // Exactly one switch, to the display the person came back to, and none while they were away.
        var fired = Assert.Single(run.Switches);
        Assert.Equal(target, fired.Display);
        Assert.True(fired.Time > returnTime);

        // It fires at the dwell plus at most one sample interval after the first qualifying sample...
        var firstFace = thinned.FindIndex(s => s.Timestamp >= returnTime);
        Assert.True(firstFace >= 0);
        var qualifying = FirstQualifying(thinned, run, target, firstFace);
        Assert.NotNull(qualifying);
        var latency = fired.Time - thinned[qualifying.Value].Timestamp;
        // Power constrained, samples are 1 or 2 frames apart, so a sample that does not support the dwell at its mark
        // costs up to 2 frames.
        var sampleInterval = powerConstrained ? 2 * Frame : Frame;
        Assert.True(latency >= Settings.Dwell - SwitchStateMachine.TimeTolerance);
        Assert.True(latency <= Settings.Dwell + sampleInterval + SwitchStateMachine.TimeTolerance);

        // ...and within 1 s plus the dwell of the face returning.
        Assert.True(fired.Time - returnTime <= 1 + Settings.Dwell);
    }

    [Theory(DisplayName = "A face that returns to the focused display never switches")]
    [MemberData(nameof(ConstrainedBySeed))]
    public void ReturningToTheFocusedDisplayDoesNothing(bool powerConstrained, ulong seed)
    {
        var profile = ScenarioWorld.Profile(seed);
        var origin = ScenarioWorld.Middle;
        var script = new PoseScript(ScenarioWorld.TargetOf(origin), seed + 23);
        script.Hold(ScenarioWorld.TargetOf(origin), 2);
        var away = script.Hold(ScenarioWorld.TargetOf(origin), 10);
        script.Hold(ScenarioWorld.TargetOf(origin), 3);
        var frames = Shifted(Blanked(script.Samples, away));

        var thinned = new AdaptiveRateModel(powerConstrained).Thin(frames);
        var run = ScenarioRunner.Run(thinned, profile, origin);

        Assert.True(thinned.Count < frames.Count - 40);
        Assert.Empty(run.Switches);
        // While away every sample says no face.
        var awayReasons = thinned.Zip(run.Decisions).Where(pair => pair.First.Pose is null).Select(pair => pair.Second.Reason).ToList();
        Assert.NotEmpty(awayReasons);
        Assert.All(awayReasons, reason => Assert.Equal(SwitchHoldReason.NoFace, reason));
    }

    // Power constrained, face present

    [Theory(DisplayName = "Power constrained with a face present and 2% face drops, 20 looks still switch, within dwell + 2 frames")]
    [MemberData(nameof(SeedList))]
    public void PowerConstrainedSwitchingWithAFace(ulong seed)
    {
        var profile = ScenarioWorld.Profile(seed);
        DisplayId[] displays = [ScenarioWorld.Left, ScenarioWorld.Right];
        var script = new PoseScript(ScenarioWorld.TargetOf(displays[1]), seed + 24);
        script.Hold(ScenarioWorld.TargetOf(displays[1]), 1);
        var looks = new List<(DisplayId Target, IndexRange Window)>();
        for (var look = 0; look < 20; look++)
        {
            var target = displays[look % 2];
            var turn = script.Turn(ScenarioWorld.TargetOf(target), 0.3);
            var hold = script.Hold(ScenarioWorld.TargetOf(target), 2, dropRate: 0.02);
            looks.Add((target, new IndexRange(turn.Lower, hold.Upper)));
        }

        var frames = Shifted(script.Samples);
        var thinned = new AdaptiveRateModel(true).Thin(frames);
        var run = ScenarioRunner.Run(thinned, profile, displays[1]);

        // Two of every three frames of the 15 fps device: exactly 10 fps.
        Assert.True(Math.Abs(thinned.Count - (frames.Count * 2 / 3)) <= 1);
        // The drops are really there, and some of them in the thinned stream.
        Assert.True(frames.Count(f => f.Pose is null) > 0);
        Assert.True(thinned.Count(f => f.Pose is null) > 0);

        Assert.Equal(20, run.Switches.Count);
        foreach (var look in looks)
        {
            var lookStart = frames[look.Window.Lower].Timestamp;
            var lookEnd = frames[look.Window.Upper - 1].Timestamp;
            var fired = run.Switches.First(s => s.Time >= lookStart && s.Time <= lookEnd);
            Assert.Equal(look.Target, fired.Display);
            var from = thinned.FindIndex(s => s.Timestamp >= lookStart);
            Assert.True(from >= 0);
            var qualifying = FirstQualifying(thinned, run, look.Target, from);
            Assert.NotNull(qualifying);
            var latency = fired.Time - thinned[qualifying.Value].Timestamp;
            // The dwell mark (0.4 s, 6 frames) is always a sample at 10 fps. If a dropped face is on it, the next
            // sample is at most 2 frames later; two gaps are 0.2 s, so the dwell is not cancelled (maxGap 0.25 s).
            Assert.True(latency >= Settings.Dwell - SwitchStateMachine.TimeTolerance);
            Assert.True(latency <= Settings.Dwell + (2 * Frame) + SwitchStateMachine.TimeTolerance);
        }
    }
}
