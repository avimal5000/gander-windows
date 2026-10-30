namespace Sway.Core.Tests;

/// <summary>Port of the Mac ProfileFitterTests.</summary>
public class ProfileFitterTests
{
    private static readonly DisplayId A = "A";
    private static readonly DisplayId B = "B";
    private static readonly DisplayId C = "C";
    private readonly PoseSwitchEngine _engine = new();

    private CalibrationProfile Fit(
        IEnumerable<TraceSegment> segments, DisplayLayout? layout = null, PoseFeatureSet features = PoseFeatureSet.VisionAngles, IReadOnlySet<int>? rounds = null) =>
        _engine.FitProfile(TraceFactory.Trace(segments, layout), features, rounds);

    private static TraceSegment Seg(DisplayId? display, int count, double yaw, double pitch, int round = 1, double yawSpread = 1, double pitchSpread = 1) =>
        TraceFactory.Segment(display, count, yaw, pitch, round, yawSpread, pitchSpread);

    private static T Req<T>(T? value)
        where T : class
    {
        Assert.NotNull(value);
        return value;
    }

    private static IReadOnlyList<DisplayId> Ids(CalibrationProfile profile) => profile.Displays.Select(d => d.DisplayId).ToList();

    // Means and standard deviations

    [Fact]
    public void MeansAndSampleStandardDeviationsAreExact()
    {
        // 40 alternating samples: mean = center, std = spread * sqrt(40 / 39) (n - 1 denominator).
        var profile = Fit([
            Seg(A, 40, 10, 5, yawSpread: 3, pitchSpread: 2),
            Seg(B, 50, -20, 12, yawSpread: 1, pitchSpread: 4),
        ]);
        var first = Req(profile.ProfileFor(A));
        var second = Req(profile.ProfileFor(B));
        Assert.Equal(10, first.Yaw, 1e-12);
        Assert.Equal(5, first.Pitch, 1e-12);
        Assert.Equal(3 * Math.Sqrt(40.0 / 39.0), first.YawStd, 1e-12);
        Assert.Equal(2 * Math.Sqrt(40.0 / 39.0), first.PitchStd, 1e-12);
        Assert.Equal(40, first.SampleCount);
        Assert.Equal(-20, second.Yaw, 1e-12);
        Assert.Equal(12, second.Pitch, 1e-12);
        Assert.Equal(Math.Sqrt(50.0 / 49.0), second.YawStd, 1e-12);
        Assert.Equal(4 * Math.Sqrt(50.0 / 49.0), second.PitchStd, 1e-12);
        Assert.Equal(50, second.SampleCount);
    }

    [Fact]
    public void SegmentsOfSeveralRoundsForOneDisplayArePooled()
    {
        var profile = Fit([
            Seg(A, 20, 10, 0, round: 1),
            Seg(B, 40, 30, 0, round: 1),
            Seg(A, 20, 20, 0, round: 2),
        ]);
        var pooled = Req(profile.ProfileFor(A));
        Assert.Equal(40, pooled.SampleCount);
        Assert.Equal(15, pooled.Yaw, 1e-12);
    }

    [Fact]
    public void LandmarkFeaturesUseLandmarkAngles()
    {
        var profile = Fit([Seg(A, 40, 10, 5), Seg(B, 40, 30, 7)], features: PoseFeatureSet.LandmarkAngles);
        Assert.Equal(PoseFeatureSet.LandmarkAngles, profile.Features);
        var first = Req(profile.ProfileFor(A));
        // The factory sets landmark angles to 0.9 * the Vision angles.
        Assert.Equal(9, first.Yaw, 1e-12);
        Assert.Equal(4.5, first.Pitch, 1e-12);
    }

    // Scales

    [Fact]
    public void ScalesPoolTheStandardDeviationsAsRootMeanSquare()
    {
        var profile = Fit([
            Seg(A, 40, 0, 0, yawSpread: 3, pitchSpread: 6),
            Seg(B, 40, 30, 0, yawSpread: 5, pitchSpread: 8),
        ]);
        var expectedYaw = Math.Sqrt((9 + 25) / 2.0 * (40.0 / 39.0));
        var expectedPitch = Math.Sqrt((36 + 64) / 2.0 * (40.0 / 39.0));
        Assert.Equal(expectedYaw, profile.YawScale, 1e-12);
        Assert.Equal(expectedPitch, profile.PitchScale, 1e-12);
    }

    [Fact]
    public void ScalesAreFlooredWhenTheCalibrationIsVeryStill()
    {
        var profile = Fit([
            Seg(A, 40, 0, 0, yawSpread: 0.1, pitchSpread: 0.1),
            Seg(B, 40, 30, 0, yawSpread: 0.1, pitchSpread: 0.1),
        ]);
        Assert.Equal(1.5, profile.YawScale);
        Assert.Equal(2.5, profile.PitchScale);
    }

    [Fact]
    public void PitchScaleNeverFallsBelowYawScale()
    {
        // Yaw spread 4 (yawScale about 4.05) with tiny pitch spread: pitchScale is lifted to yawScale.
        var profile = Fit([
            Seg(A, 40, 0, 0, yawSpread: 4, pitchSpread: 1),
            Seg(B, 40, 30, 0, yawSpread: 4, pitchSpread: 1),
        ]);
        Assert.True(profile.YawScale > 4);
        Assert.Equal(profile.YawScale, profile.PitchScale);
    }

    [Fact]
    public void PitchScaleCanExceedYawScale()
    {
        var profile = Fit([
            Seg(A, 40, 0, 0, yawSpread: 1, pitchSpread: 6),
            Seg(B, 40, 30, 0, yawSpread: 1, pitchSpread: 6),
        ]);
        Assert.Equal(1.5, profile.YawScale);
        Assert.True(profile.PitchScale > 6);
    }

    // Which displays and samples count

    [Fact]
    public void DisplayWithFewerThanThirtyPointsIsDropped()
    {
        var profile = Fit([Seg(A, 40, 10, 0), Seg(B, 29, 20, 0), Seg(C, 40, 30, 0)]);
        Assert.Equal([A, C], Ids(profile));
    }

    [Fact]
    public void DisplayWithExactlyThirtyPointsIsKept()
    {
        var profile = Fit([Seg(A, 30, 10, 0), Seg(B, 30, 20, 0)]);
        Assert.Equal([A, B], Ids(profile));
    }

    [Fact]
    public void FramesWithoutAFaceDoNotCountTowardsThePointMinimum()
    {
        var mixed = new TraceSegment(
            B, 1, Seg(B, 29, 20, 0).Samples.Concat(TraceFactory.EmptySegment(B, 30).Samples).ToList());
        var profile = Fit([Seg(A, 40, 10, 0), mixed, Seg(C, 40, 30, 0)]);
        Assert.Equal([A, C], Ids(profile));
    }

    [Fact]
    public void UnlabeledSegmentsAreIgnored()
    {
        var profile = Fit([Seg(null, 100, 99, 99, round: 0), Seg(A, 40, 10, 0), Seg(B, 40, 20, 0)]);
        Assert.Equal([A, B], Ids(profile));
        Assert.All(profile.Displays, d => Assert.True(d.Yaw < 50));
    }

    [Fact]
    public void UnlabeledSegmentsAreNotUsed()
    {
        var trace = TraceFactory.Trace([Seg(null, 100, 10, 0, round: 0), TraceFactory.EmptySegment(A, 40)]);
        var error = Assert.Throws<CalibrationException>(() => _engine.FitProfile(trace, PoseFeatureSet.VisionAngles, null));
        Assert.Equal(CalibrationFailure.TooFewDisplays, error.Failure);
    }

    [Fact]
    public void RoundsFilterSelectsOnlyThoseRounds()
    {
        TraceSegment[] segments =
        [
            Seg(A, 40, 10, 0, round: 1),
            Seg(B, 40, 30, 0, round: 1),
            Seg(A, 40, 14, 0, round: 2),
            Seg(B, 40, 36, 0, round: 2),
        ];
        var first = Fit(segments, rounds: new HashSet<int> { 1 });
        var second = Fit(segments, rounds: new HashSet<int> { 2 });
        var both = Fit(segments, rounds: new HashSet<int> { 1, 2 });
        var all = Fit(segments, rounds: null);
        Assert.Equal(10, Req(first.ProfileFor(A)).Yaw, 1e-12);
        Assert.Equal(30, Req(first.ProfileFor(B)).Yaw, 1e-12);
        Assert.Equal(14, Req(second.ProfileFor(A)).Yaw, 1e-12);
        Assert.Equal(36, Req(second.ProfileFor(B)).Yaw, 1e-12);
        Assert.Equal(12, Req(all.ProfileFor(A)).Yaw, 1e-12);
        Assert.Equal(all, both);
        Assert.All(first.Displays, d => Assert.Equal(40, d.SampleCount));
        Assert.All(all.Displays, d => Assert.Equal(80, d.SampleCount));
    }

    [Fact]
    public void RoundsFilterThatSelectsNothingIsTooFewDisplays()
    {
        var trace = TraceFactory.Trace([Seg(A, 40, 10, 0, round: 1), Seg(B, 40, 30, 0, round: 1)]);
        var error = Assert.Throws<CalibrationException>(() => _engine.FitProfile(trace, PoseFeatureSet.VisionAngles, new HashSet<int> { 5 }));
        Assert.Equal(CalibrationFailure.TooFewDisplays, error.Failure);
    }

    // Errors

    [Fact]
    public void LandmarksMissingFromTheTraceReportMissingFeatures()
    {
        var noLandmarks = new[] { A, B }.Select(id => new TraceSegment(
            id, 1, Enumerable.Range(0, 40).Select(i => new TimedPose((double)i / 15, Poses.Make(10, 0))).ToList())).ToList();
        var trace = TraceFactory.Trace(noLandmarks);
        var error = Assert.Throws<CalibrationException>(() => _engine.FitProfile(trace, PoseFeatureSet.LandmarkAngles, null));
        Assert.Equal(CalibrationFailure.FeaturesMissing, error.Failure);
        Assert.Equal(PoseFeatureSet.LandmarkAngles, error.Features);
    }

    [Fact]
    public void APoseWithOnlyOneLandmarkAngleDoesNotHaveTheFeatures()
    {
        var halfLandmarks = new[] { A, B }.Select(id => new TraceSegment(
            id, 1, Enumerable.Range(0, 40).Select(i => new TimedPose((double)i / 15, Poses.Make(10, 0, landmarkYaw: 3))).ToList())).ToList();
        var trace = TraceFactory.Trace(halfLandmarks);
        var error = Assert.Throws<CalibrationException>(() => _engine.FitProfile(trace, PoseFeatureSet.LandmarkAngles, null));
        Assert.Equal(CalibrationFailure.FeaturesMissing, error.Failure);
    }

    /// <summary>No faces at all (camera covered) is too little data, not missing features: every face has Vision angles.</summary>
    [Fact]
    public void ATraceWithNoFacesOrNoSegmentsIsTooFewDisplays()
    {
        var noFaces = TraceFactory.Trace([TraceFactory.EmptySegment(A, 50), TraceFactory.EmptySegment(B, 50)]);
        Assert.Equal(CalibrationFailure.TooFewDisplays, Assert.Throws<CalibrationException>(() => _engine.FitProfile(noFaces, PoseFeatureSet.VisionAngles, null)).Failure);
        var empty = TraceFactory.Trace([]);
        Assert.Equal(CalibrationFailure.TooFewDisplays, Assert.Throws<CalibrationException>(() => _engine.FitProfile(empty, PoseFeatureSet.VisionAngles, null)).Failure);
    }

    [Fact]
    public void OneUsableDisplayIsTooFew()
    {
        var trace = TraceFactory.Trace([Seg(A, 100, 10, 0), Seg(B, 29, 30, 0)]);
        Assert.Equal(CalibrationFailure.TooFewDisplays, Assert.Throws<CalibrationException>(() => _engine.FitProfile(trace, PoseFeatureSet.VisionAngles, null)).Failure);
    }

    [Fact]
    public void TwoDisplaysThatAreEachJustUnderTheMinimumAreTooFew()
    {
        var trace = TraceFactory.Trace([Seg(A, 29, 10, 0), Seg(B, 29, 30, 0)]);
        Assert.Equal(CalibrationFailure.TooFewDisplays, Assert.Throws<CalibrationException>(() => _engine.FitProfile(trace, PoseFeatureSet.VisionAngles, null)).Failure);
    }

    // Ordering and bookkeeping

    [Fact]
    public void DisplaysFollowTheLayoutOrderNotTheSegmentOrder()
    {
        var layout = Poses.Layout(C, B, A);
        var profile = Fit([Seg(A, 40, 10, 0), Seg(B, 40, 20, 0), Seg(C, 40, 30, 0)], layout);
        Assert.Equal(layout.Displays.Select(d => d.Id), Ids(profile));
        Assert.Equal([C, B, A], Ids(profile));
    }

    [Fact]
    public void DisplaysMissingFromTheLayoutFollowInOrderOfFirstAppearance()
    {
        DisplayId extra1 = "X";
        DisplayId extra2 = "Y";
        var layout = Poses.Layout(B, A, C); // C has no segment: ignored
        var profile = Fit(
            [Seg(extra1, 40, 1, 0), Seg(A, 40, 10, 0), Seg(extra2, 40, 2, 0), Seg(B, 40, 20, 0)],
            layout);
        Assert.Equal([B, A, extra1, extra2], Ids(profile));
    }

    [Fact]
    public void ATraceWithoutALayoutKeepsFirstAppearanceOrderAndAnEmptyFingerprint()
    {
        var profile = Fit([Seg(B, 40, 20, 0), Seg(A, 40, 10, 0)]);
        Assert.Equal([B, A], Ids(profile));
        Assert.Equal(string.Empty, profile.LayoutFingerprint);
    }

    [Fact]
    public void BookkeepingFieldsComeFromTheTraceAndTheCall()
    {
        var layout = Poses.Layout(A, B);
        var profile = Fit([Seg(A, 40, 10, 0), Seg(B, 40, 20, 0)], layout, PoseFeatureSet.LandmarkAngles);
        Assert.Equal(layout.Fingerprint, profile.LayoutFingerprint);
        Assert.Equal("Test camera", profile.CameraName);
        Assert.Equal(TraceFactory.CreatedAt, profile.CreatedAt);
        Assert.Equal(PoseFeatureSet.LandmarkAngles, profile.Features);
        Assert.Equal(CalibrationProfile.CurrentFormatVersion, profile.FormatVersion);
        Assert.True(profile.Matches(layout));
    }

    [Fact]
    public void FittingIsDeterministic()
    {
        var rng = new SplitMix64(99);
        var segments = new[] { A, B, C }.Select((id, index) =>
        {
            var samples = Enumerable.Range(0, 60).Select(frame =>
            {
                var yaw = (index * 20) + (rng.NextGaussian() * 1.5);
                var pitch = 5 + (rng.NextGaussian() * 2.5);
                return new TimedPose((double)frame / 15, Poses.Make(yaw, pitch));
            }).ToList();
            return new TraceSegment(id, 1, samples);
        }).ToList();
        var layout = Poses.Layout(A, B, C);
        Assert.Equal(Fit(segments, layout), Fit(segments, layout));
    }
}
