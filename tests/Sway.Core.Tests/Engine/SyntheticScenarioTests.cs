namespace Sway.Core.Tests;

/// <summary>
/// Port of the Mac SyntheticScenarioTests: the six synthetic scenarios, at 15 fps, with the default settings and a
/// profile fitted (by the real engine) from a synthetic labeled trace. The expected counts are the contract's; nothing
/// here is tuned to pass. Each scenario runs for several seeds so a lucky noise draw cannot hide a problem.
/// </summary>
public class SyntheticScenarioTests
{
    private static readonly ulong[] Seeds = [11, 22, 33];
    private static readonly int[][] Alternations = [[0, 2], [0, 1], [1, 2]];
    private static readonly SwitchSettings Settings = SwitchSettings.Default;
    private const double Frame = 1 / ScenarioWorld.FrameRate;

    public static IEnumerable<object[]> IndexAndSeed(int count) =>
        Enumerable.Range(0, count).SelectMany(i => Seeds.Select(s => new object[] { i, s }));

    public static IEnumerable<object[]> ThreeDisplaysBySeed() => IndexAndSeed(3);

    public static IEnumerable<object[]> TwoPairsBySeed() => IndexAndSeed(2);

    public static IEnumerable<object[]> AlternationsBySeed() =>
        Alternations.SelectMany(pair => Seeds.Select(s => new object[] { pair, s }));

    public static IEnumerable<object[]> DirectionBySeed() =>
        new[] { true, false }.SelectMany(d => Seeds.Select(s => new object[] { d, s }));

    // 1. Noise at one display

    [Theory(DisplayName = "120 s at one display with twice-the-std noise and 2% face drops: no switches")]
    [MemberData(nameof(ThreeDisplaysBySeed))]
    public void NoiseAtOneDisplayNeverSwitches(int displayIndex, ulong seed)
    {
        var profile = ScenarioWorld.Profile(seed);
        var display = ScenarioWorld.Ids[displayIndex];
        var script = new PoseScript(ScenarioWorld.TargetOf(display), seed + 1);
        script.Hold(ScenarioWorld.TargetOf(display), 120, noise: 2, dropRate: 0.02);
        var run = ScenarioRunner.Run(script.Samples, profile, display);

        Assert.Empty(run.Switches);
        // The drops are really there, and about 2% of the frames.
        var drops = script.Samples.Count(s => s.Pose is null);
        Assert.True(drops > 0);
        Assert.True((double)drops / script.Samples.Count < 0.05);
    }

    // 2. Ambiguous midpoint

    [Theory(DisplayName = "60 s at the midpoint between adjacent displays, half-std noise: no switches")]
    [MemberData(nameof(TwoPairsBySeed))]
    public void MidpointBetweenAdjacentDisplaysNeverSwitches(int pairIndex, ulong seed)
    {
        var profile = ScenarioWorld.Profile(seed);
        var first = profile.Displays[pairIndex];
        var second = profile.Displays[pairIndex + 1];
        var midpoint = new PoseScript.Target((first.Yaw + second.Yaw) / 2, (first.Pitch + second.Pitch) / 2);
        var script = new PoseScript(midpoint, seed + 2);
        script.Hold(midpoint, 60, noise: 0.5);
        // Focus unknown, so the margin alone has to hold the line.
        var run = ScenarioRunner.Run(script.Samples, profile, null);

        Assert.Empty(run.Switches);
    }

    // 3. Glances

    [Theory(DisplayName = "20 glances of 0.25 s to another display: no switches")]
    [MemberData(nameof(ThreeDisplaysBySeed))]
    public void GlancesNeverSwitch(int baseIndex, ulong seed)
    {
        var profile = ScenarioWorld.Profile(seed);
        var baseDisplay = ScenarioWorld.Ids[baseIndex];
        var others = ScenarioWorld.Ids.Where(id => id != baseDisplay).ToList();
        var script = new PoseScript(ScenarioWorld.TargetOf(baseDisplay), seed + 3);
        script.Hold(ScenarioWorld.TargetOf(baseDisplay), 1);
        for (var glance = 0; glance < 20; glance++)
        {
            script.Hold(ScenarioWorld.TargetOf(others[glance % 2]), 0.25);
            script.Hold(ScenarioWorld.TargetOf(baseDisplay), 1);
        }

        var run = ScenarioRunner.Run(script.Samples, profile, baseDisplay);

        Assert.Empty(run.Switches);
        // The glances were seen as dwells that ended early, not ignored altogether.
        Assert.Contains(run.Decisions, d => d.Phase is DwellingPhase);
    }

    // 4. Intentional looks

    [Theory(DisplayName = "20 intentional looks switch exactly 20 times, each within dwell + 1 frame")]
    [MemberData(nameof(AlternationsBySeed))]
    public void IntentionalLooksSwitchOncePerLook(int[] pair, ulong seed)
    {
        var profile = ScenarioWorld.Profile(seed);
        var displays = pair.Select(i => ScenarioWorld.Ids[i]).ToArray();
        var script = new PoseScript(ScenarioWorld.TargetOf(displays[1]), seed + 4);
        script.Hold(ScenarioWorld.TargetOf(displays[1]), 1);
        var looks = new List<(DisplayId Target, IndexRange Window)>();
        for (var look = 0; look < 20; look++)
        {
            var target = displays[look % 2]; // first look goes to displays[0], then back, and so on
            var turn = script.Turn(ScenarioWorld.TargetOf(target), 0.3);
            var hold = script.Hold(ScenarioWorld.TargetOf(target), 2);
            looks.Add((target, new IndexRange(turn.Lower, hold.Upper)));
        }

        var run = ScenarioRunner.Run(script.Samples, profile, displays[1]);

        Assert.Equal(20, run.Switches.Count);
        foreach (var look in looks)
        {
            var inLook = run.Switches.Where(s => look.Window.Contains(s.Index)).ToList();
            var fired = Assert.Single(inLook);
            Assert.Equal(look.Target, fired.Display);

            // First sample of the look that points at the target with at least the enter margin.
            var start = Enumerable.Range(look.Window.Lower, look.Window.Count).First(index =>
            {
                var classification = run.Decisions[index].Classification;
                return classification.Kind == PoseClassification.ClassKind.Display && classification.Best == look.Target
                    && classification.Margin >= Settings.EnterMargin;
            });
            var latency = fired.Time - script.Samples[start].Timestamp;
            Assert.True(latency >= Settings.Dwell - SwitchStateMachine.TimeTolerance);
            Assert.True(latency <= Settings.Dwell + Frame);
        }
    }

    // 5. Transit through the middle display

    [Theory(DisplayName = "0.25 s transit across the middle display: exactly one switch, to the far display")]
    [MemberData(nameof(DirectionBySeed))]
    public void TransitThroughTheMiddleDisplaySwitchesOnlyToTheFarOne(bool leftToRight, ulong seed)
    {
        var profile = ScenarioWorld.Profile(seed);
        var origin = leftToRight ? ScenarioWorld.Left : ScenarioWorld.Right;
        var destination = leftToRight ? ScenarioWorld.Right : ScenarioWorld.Left;
        var script = new PoseScript(ScenarioWorld.TargetOf(origin), seed + 5);
        script.Hold(ScenarioWorld.TargetOf(origin), 1);
        script.Turn(ScenarioWorld.TargetOf(destination), 0.25);
        script.Hold(ScenarioWorld.TargetOf(destination), 2);
        var run = ScenarioRunner.Run(script.Samples, profile, origin);

        Assert.Equal([destination], run.Switches.Select(s => s.Display));
        // The transit really did start a dwell on the middle display, which was then abandoned.
        Assert.Contains(run.Decisions, d => d.Phase is DwellingPhase { Display: var display } && display == ScenarioWorld.Middle);
    }

    // 6. Keyboard

    [Theory(DisplayName = "A 2 s look 40 degrees down: no switches, and the reason is elsewhere")]
    [MemberData(nameof(ThreeDisplaysBySeed))]
    public void LookingDownAtTheKeyboardIsElsewhere(int displayIndex, ulong seed)
    {
        var profile = ScenarioWorld.Profile(seed);
        var display = ScenarioWorld.Ids[displayIndex];
        var screen = ScenarioWorld.TargetOf(display);
        var keyboard = new PoseScript.Target(screen.Yaw, screen.Pitch - 40);
        var script = new PoseScript(screen, seed + 6);
        script.Hold(screen, 1);
        script.Turn(keyboard, 0.3);
        var look = script.Hold(keyboard, 2);
        script.Turn(screen, 0.3);
        script.Hold(screen, 1);
        var run = ScenarioRunner.Run(script.Samples, profile, display);

        Assert.Empty(run.Switches);
        Assert.Equal(30, look.Count);
        for (var index = look.Lower; index < look.Upper; index++)
        {
            Assert.Equal(SwitchHoldReason.Elsewhere, run.Decisions[index].Reason);
            Assert.Equal(PoseClassification.ClassKind.Elsewhere, run.Decisions[index].Classification.Kind);
        }
    }
}
