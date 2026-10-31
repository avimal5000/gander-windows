namespace Gander.Core.Tests;

/// <summary>
/// Port of the Mac RecordedTraceTests. The user's real M1 gate recording (numbers only): 3 displays x 2 rounds x 75
/// samples, segments 1.6 s apart, recorded on a C922 on 2026-10-24. Fit on one round, replay the other.
/// </summary>
public class RecordedTraceTests
{
    private static readonly SwitchSettings Settings = SwitchSettings.Default;
    private const double OneFrame = 1 / GanderPolicy.DefaultFrameRate;
    private readonly PoseSwitchEngine _engine = new();

    private static List<TraceSegment> SegmentsOf(LabeledPoseTrace trace, int round) => trace.Segments.Where(s => s.Round == round).ToList();

    public static IEnumerable<object[]> FitAndReplay() => [[1, 2], [2, 1]];

    [Fact]
    public void FixtureHasTheDocumentedShape()
    {
        var trace = GateTraceFixture.Load();
        Assert.Equal(6, trace.Segments.Count);
        Assert.All(trace.Segments, s =>
        {
            Assert.Equal(75, s.Samples.Count);
            Assert.NotNull(s.DisplayId);
        });
        Assert.Equal(3, trace.Segments.Select(s => s.DisplayId).Distinct().Count());
        Assert.Equal(3, SegmentsOf(trace, 1).Count);
        Assert.Equal(3, SegmentsOf(trace, 2).Count);
        Assert.Equal(3, trace.Layout?.Displays.Count);
        Assert.Equal("C922 Pro Stream Webcam", trace.CameraName);
    }

    /// <summary>Not in the Swift suite: the layout fingerprint computed here equals the one the Mac app recorded.</summary>
    [Fact]
    public void TheRecordedLayoutFingerprintIsReproduced() =>
        Assert.Equal(GateTraceFixture.RecordedFingerprint, GateTraceFixture.Load().Layout?.Fingerprint);

    [Theory]
    [MemberData(nameof(FitAndReplay))]
    public void ReplayingTheOtherRoundSwitchesExactlyTwiceAtTheRightTimes(int fit, int replay)
    {
        var trace = GateTraceFixture.Load();
        var profile = _engine.FitProfile(trace, PoseFeatureSet.VisionAngles, new HashSet<int> { fit });
        Assert.Equal(3, profile.Displays.Count);

        var roundSegments = SegmentsOf(trace, replay);
        var first = roundSegments[0].DisplayId!.Value;
        var decider = _engine.MakeDecider(profile, Settings);
        DisplayId? focused = first;
        var switches = new List<(double Time, DisplayId Display)>();
        foreach (var segment in roundSegments)
        {
            foreach (var sample in segment.Samples)
            {
                var decision = decider.Process(sample, new SwitchContext(focused));
                if (decision.SwitchTo is { } target)
                {
                    switches.Add((sample.Timestamp, target));
                    focused = target;
                }
            }
        }

        // Exactly two switches: to the 2nd and the 3rd segment's display, and no others.
        Assert.Equal(2, switches.Count);
        var expected = roundSegments.Skip(1).Select(s => s.DisplayId!.Value).ToList();
        Assert.Equal(expected, switches.Select(s => s.Display).ToList());
        foreach (var (fired, segment) in switches.Zip(roundSegments.Skip(1)))
        {
            var latency = fired.Time - segment.Samples[0].Timestamp;
            Assert.True(latency >= Settings.Dwell - SwitchStateMachine.TimeTolerance);
            Assert.True(latency <= Settings.Dwell + OneFrame);
        }
    }

    [Theory]
    [MemberData(nameof(FitAndReplay))]
    public void AtLeastNinetyNinePercentOfTheHeldOutRoundClassifiesAsItsOwnDisplay(int fit, int test)
    {
        var trace = GateTraceFixture.Load();
        var profile = _engine.FitProfile(trace, PoseFeatureSet.VisionAngles, new HashSet<int> { fit });
        var total = 0;
        var correct = 0;
        foreach (var segment in SegmentsOf(trace, test))
        {
            foreach (var sample in segment.Samples)
            {
                total++;
                var result = _engine.Classify(sample.Pose, profile, Settings);
                if (result.Kind == PoseClassification.ClassKind.Display && result.Best == segment.DisplayId)
                {
                    correct++;
                }
            }
        }

        Assert.Equal(225, total);
        Assert.True((double)correct / total >= 0.99);
    }

    [Fact]
    public void TheProfileMatchesTheRecordedLayoutAndCamera()
    {
        var trace = GateTraceFixture.Load();
        var layout = trace.Layout!;
        var profile = _engine.FitProfile(trace, PoseFeatureSet.VisionAngles, null);
        Assert.True(profile.Matches(layout));
        Assert.Equal(trace.CameraName, profile.CameraName);
        Assert.Equal(layout.Displays.Select(d => d.Id), profile.Displays.Select(d => d.DisplayId));
        Assert.True(profile.PitchScale >= profile.YawScale);
    }
}
