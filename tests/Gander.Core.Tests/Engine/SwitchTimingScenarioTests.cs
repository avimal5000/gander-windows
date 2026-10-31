namespace Gander.Core.Tests;

/// <summary>
/// Port of the Mac SwitchTimingScenarioTests: the adjustable switching timing (SPEC D49, F35) seen from the decider, at
/// 15 fps with a profile fitted by the real engine; the settings come from <see cref="SwitchSensitivity"/>.
/// A quick look back switches cooldown + dwell after the first switch, each rounded up to whole frames: the default is
/// 0.75 s + 0.4 s (18 frames, 1.2 s) and the Instant preset 0.3 s + 0.1 s (7 frames, about 0.47 s).
/// </summary>
public class SwitchTimingScenarioTests
{
    private const double Frame = 1 / ScenarioWorld.FrameRate;
    private static readonly SwitchSettings DefaultTiming = SwitchSensitivity.Default.ToSwitchSettings();
    private static readonly SwitchSettings InstantTiming = SwitchSensitivity.Instant.ToSwitchSettings();

    public static IEnumerable<object[]> Seeds() => new ulong[] { 11, 22, 33 }.Select(s => new object[] { s });

    public static IEnumerable<object[]> DirectionBySeed() =>
        new[] { true, false }.SelectMany(d => new ulong[] { 11, 22, 33 }.Select(s => new object[] { d, s }));

    /// <summary>
    /// The head goes to the middle display and, on the sample after the first switch fires, turns back to the right
    /// display and stays there. So the cooldown, not the script, decides when the second switch can start. Returns the
    /// seconds from the first switch to the second.
    /// </summary>
    private static double QuickReturnGap(SwitchSettings settings, ulong seed)
    {
        var profile = ScenarioWorld.Profile(seed);
        var home = ScenarioWorld.Right;
        var away = ScenarioWorld.Middle;
        PoseScript ScriptUpToTheLook()
        {
            var s = new PoseScript(ScenarioWorld.TargetOf(home), seed + 7);
            s.Hold(ScenarioWorld.TargetOf(home), 1);
            s.Turn(ScenarioWorld.TargetOf(away), 0.1);
            return s;
        }

        // Where the first switch fires when the head stays on the middle display.
        var probe = ScriptUpToTheLook();
        probe.Hold(ScenarioWorld.TargetOf(away), 3);
        var probed = ScenarioRunner.Run(probe.Samples, profile, home, settings);
        var first = probed.Switches.First();

        // The same samples up to that switch, then the look back.
        var script = ScriptUpToTheLook();
        var awayFrames = first.Index + 1 - script.Samples.Count;
        Assert.True(awayFrames >= 0);
        script.Hold(ScenarioWorld.TargetOf(away), Math.Max(0, awayFrames) / ScenarioWorld.FrameRate);
        var turnBack = script.Turn(ScenarioWorld.TargetOf(home), 0.1);
        script.Hold(ScenarioWorld.TargetOf(home), 4);
        var run = ScenarioRunner.Run(script.Samples, profile, home, settings);

        Assert.Equal([away, home], run.Switches.Select(s => s.Display));
        Assert.Equal(first, run.Switches[0]); // Nothing before the look back changed
        Assert.Equal(first.Index + 1, turnBack.Lower); // The look back follows the first switch at once
        return run.Switches[1].Time - run.Switches[0].Time;
    }

    // A quick look back

    [Theory(DisplayName = "Default timing: a held look back switches back about cooldown + dwell after the first switch")]
    [MemberData(nameof(Seeds))]
    public void AQuickReturnWithTheDefaultTimingTakesTheCooldownPlusTheDwell(ulong seed)
    {
        Assert.Equal(SwitchSettings.Default, DefaultTiming);
        var gap = QuickReturnGap(DefaultTiming, seed);

        var expected = DefaultTiming.Cooldown + DefaultTiming.Dwell;
        Assert.True(gap >= expected - SwitchStateMachine.TimeTolerance);
        Assert.True(gap <= expected + (2 * Frame));
        Assert.True(Math.Abs(gap - 1.2) < Frame / 2, "18 frames");
    }

    [Theory(DisplayName = "Instant timing: a held look back switches back about 0.4 s after the first switch")]
    [MemberData(nameof(Seeds))]
    public void AQuickReturnWithTheInstantPresetIsAboutFourTenthsOfASecond(ulong seed)
    {
        Assert.Equal(0.10, InstantTiming.Dwell);
        Assert.Equal(0.30, InstantTiming.Cooldown);
        var gap = QuickReturnGap(InstantTiming, seed);

        var expected = InstantTiming.Cooldown + InstantTiming.Dwell;
        Assert.True(gap >= expected - SwitchStateMachine.TimeTolerance);
        Assert.True(gap <= expected + (2 * Frame));
        Assert.True(Math.Abs(gap - (7 * Frame)) < Frame / 2, "7 frames");
    }

    [Theory(DisplayName = "The Instant preset returns more than twice as fast as the default")]
    [MemberData(nameof(Seeds))]
    public void TheInstantPresetIsFasterThanTheDefault(ulong seed)
    {
        var slow = QuickReturnGap(DefaultTiming, seed);
        var fast = QuickReturnGap(InstantTiming, seed);

        Assert.True(fast < slow / 2);
        Assert.True(slow - fast > 0.6);
    }

    [Theory(DisplayName = "Every pause from the shortest to the longest delays the return by that pause")]
    [InlineData(0.2)]
    [InlineData(0.75)]
    [InlineData(1.5)]
    public void TheReturnWaitsForTheWholePause(double cooldown)
    {
        var settings = new SwitchSensitivity(0.10, cooldown, 2).ToSwitchSettings();
        Assert.Equal(cooldown, settings.Cooldown);
        var gap = QuickReturnGap(settings, 22);

        Assert.True(gap >= cooldown + 0.10 - SwitchStateMachine.TimeTolerance);
        Assert.True(gap <= cooldown + 0.10 + (2 * Frame));
    }

    // A sweep through the middle display

    [Theory(DisplayName = "0.2 s and 0.25 s sweeps across the middle display: one switch, to the far display, with the Instant preset")]
    [MemberData(nameof(DirectionBySeed))]
    public void AFastSweepStillSkipsTheMiddleDisplayWithTheInstantPreset(bool leftToRight, ulong seed)
    {
        var profile = ScenarioWorld.Profile(seed);
        var origin = leftToRight ? ScenarioWorld.Left : ScenarioWorld.Right;
        var destination = leftToRight ? ScenarioWorld.Right : ScenarioWorld.Left;
        // Each sweep is over the middle display for less than the 0.10 s hold time.
        foreach (var sweep in new[] { 0.2, 0.25 })
        {
            var script = new PoseScript(ScenarioWorld.TargetOf(origin), seed + 8);
            script.Hold(ScenarioWorld.TargetOf(origin), 1);
            script.Turn(ScenarioWorld.TargetOf(destination), sweep);
            script.Hold(ScenarioWorld.TargetOf(destination), 2);
            var run = ScenarioRunner.Run(script.Samples, profile, origin, InstantTiming);

            Assert.True(run.Switches.Select(s => s.Display).SequenceEqual([destination]), $"{sweep} s sweep");
            // The sweep really did start a dwell on the middle display, which was then abandoned.
            Assert.True(
                run.Decisions.Any(d => d.Phase is DwellingPhase { Display: var display } && display == ScenarioWorld.Middle),
                $"{sweep} s sweep");
        }
    }

    [Theory(DisplayName = "A 0.3 s rest on the middle display is a glance by default and a switch with the Instant preset")]
    [MemberData(nameof(Seeds))]
    public void AShortRestOnTheMiddleDisplayDependsOnTheHoldTime(ulong seed)
    {
        var profile = ScenarioWorld.Profile(seed);
        var script = new PoseScript(ScenarioWorld.TargetOf(ScenarioWorld.Left), seed + 9);
        script.Hold(ScenarioWorld.TargetOf(ScenarioWorld.Left), 1);
        script.Turn(ScenarioWorld.TargetOf(ScenarioWorld.Middle), 0.15);
        script.Hold(ScenarioWorld.TargetOf(ScenarioWorld.Middle), 0.3);
        script.Turn(ScenarioWorld.TargetOf(ScenarioWorld.Right), 0.15);
        script.Hold(ScenarioWorld.TargetOf(ScenarioWorld.Right), 2);

        var byDefault = ScenarioRunner.Run(script.Samples, profile, ScenarioWorld.Left, DefaultTiming);
        var instant = ScenarioRunner.Run(script.Samples, profile, ScenarioWorld.Left, InstantTiming);

        Assert.Equal([ScenarioWorld.Right], byDefault.Switches.Select(s => s.Display));
        Assert.Equal([ScenarioWorld.Middle, ScenarioWorld.Right], instant.Switches.Select(s => s.Display));
    }
}
