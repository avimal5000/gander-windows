namespace Sway.Core.Tests;

/// <summary>Port of the Mac PoseClassifierTests.</summary>
public class PoseClassifierTests
{
    private static readonly DisplayId D1 = "D1";
    private static readonly DisplayId D2 = "D2";
    private static readonly DisplayId D3 = "D3";
    private static readonly SwitchSettings Settings = SwitchSettings.Default; // elsewhere margins 20 (yaw) / 15 (pitch), min confidence 0.5
    private readonly PoseSwitchEngine _engine = new();

    /// <summary>D1 at (0, 0) and D2 at (10, 0); yawScale 2, pitchScale 4. Elsewhere box: yaw -20 to 30, pitch -15 to 15.</summary>
    private static CalibrationProfile Profile(
        PoseFeatureSet features = PoseFeatureSet.VisionAngles,
        (DisplayId, double, double)[]? displays = null,
        double yawScale = 2,
        double pitchScale = 4)
    {
        var centers = displays ?? [(D1, 0, 0), (D2, 10, 0)];
        return new CalibrationProfile(
            features,
            centers.Select(c => new DisplayProfile(c.Item1, c.Item2, c.Item3, 1, 1, 50)).ToList(),
            yawScale,
            pitchScale,
            "test",
            null,
            DateTimeOffset.UnixEpoch);
    }

    private PoseClassification Classify(HeadPose? pose, CalibrationProfile? profile = null, SwitchSettings? settings = null) =>
        _engine.Classify(pose, profile ?? Profile(), settings ?? Settings);

    // Nearest display and margin

    [Fact]
    public void NearestDisplayWinsWithAHandComputedMargin()
    {
        // Pose (2, 4): d1 = sqrt((2/2)^2 + (4/4)^2) = sqrt(2); d2 = sqrt((8/2)^2 + (4/4)^2) = sqrt(17).
        var result = Classify(Poses.Make(2, 4));
        var near = Math.Sqrt(2.0);
        var far = Math.Sqrt(17.0);
        Assert.Equal(PoseClassification.ClassKind.Display, result.Kind);
        Assert.Equal(D1, result.Best);
        Assert.Equal(D2, result.RunnerUp);
        Assert.Equal((far - near) / (far + near), result.Margin, 1e-12);
        Assert.Equal(near, result.Distances[D1], 1e-12);
        Assert.Equal(far, result.Distances[D2], 1e-12);
        Assert.Equal(2, result.Distances.Count);
    }

    [Fact]
    public void APoseAtADisplayCenterHasMarginOne()
    {
        var result = Classify(Poses.Make(10, 0));
        Assert.Equal(D2, result.Best);
        Assert.Equal(D1, result.RunnerUp);
        Assert.Equal(1, result.Margin);
        Assert.Equal(0, result.Distances[D2]);
    }

    [Fact]
    public void MarginIsZeroHalfwayBetweenTwoDisplaysAndTiesGoToTheEarlierDisplay()
    {
        var result = Classify(Poses.Make(5, 0));
        Assert.Equal(PoseClassification.ClassKind.Display, result.Kind);
        Assert.Equal(D1, result.Best);
        Assert.Equal(D2, result.RunnerUp);
        Assert.Equal(0, result.Margin);

        var reversed = Profile(displays: [(D2, 10, 0), (D1, 0, 0)]);
        var flipped = Classify(Poses.Make(5, 0), reversed);
        Assert.Equal(D2, flipped.Best);
        Assert.Equal(D1, flipped.RunnerUp);
    }

    [Fact]
    public void WhenEveryDistanceIsZeroTheMarginIsZeroNotNaN()
    {
        var stacked = Profile(displays: [(D1, 3, 3), (D2, 3, 3)]);
        var result = Classify(Poses.Make(3, 3), stacked);
        Assert.Equal(PoseClassification.ClassKind.Display, result.Kind);
        Assert.Equal(D1, result.Best);
        Assert.Equal(D2, result.RunnerUp);
        Assert.Equal(0, result.Margin);
    }

    [Fact]
    public void RunnerUpIsTheSecondNearestAmongThree()
    {
        var three = Profile(displays: [(D1, 0, 0), (D2, 10, 0), (D3, 40, 0)]);
        var result = Classify(Poses.Make(14, 0), three);
        Assert.Equal(D2, result.Best);
        Assert.Equal(D1, result.RunnerUp);
        Assert.Equal(3, result.Distances.Count);
        Assert.Equal(13, result.Distances[D3]);
    }

    [Fact]
    public void ThirdPlaceTiesDoNotDisturbBestAndRunnerUp()
    {
        // Equal distances 5 / 5 for D2 and D3 behind a nearer D1: the earlier of the tied pair is the runner-up.
        var three = Profile(displays: [(D1, 5, 0), (D2, 0, 0), (D3, 10, 0)]);
        var result = Classify(Poses.Make(5, 0), three);
        Assert.Equal(D1, result.Best);
        Assert.Equal(D2, result.RunnerUp);
        Assert.Equal(1, result.Margin);
    }

    // Axis scales

    [Fact]
    public void TheSameAngularOffsetCountsLessInPitchThanInYaw()
    {
        // yawScale 2, pitchScale 4: 4 degrees is 2 scale units in yaw but only 1 in pitch.
        var yawOffset = Classify(Poses.Make(4, 0));
        var pitchOffset = Classify(Poses.Make(0, 4));
        Assert.Equal(2, yawOffset.Distances[D1]);
        Assert.Equal(1, pitchOffset.Distances[D1]);
        Assert.True(pitchOffset.Distances[D1] < yawOffset.Distances[D1]);
    }

    // Elsewhere

    [Theory]
    [InlineData(-20.5, 0.0)] // left of the lowest yaw (0 - 20)
    [InlineData(30.5, 0.0)] // right of the highest yaw (10 + 20)
    [InlineData(5.0, -15.5)] // below the lowest pitch (0 - 15)
    [InlineData(5.0, 15.5)] // above the highest pitch (0 + 15)
    [InlineData(100.0, 100.0)] // far off in both
    public void PosesOutsideTheBoxAreElsewhere(double yaw, double pitch)
    {
        var result = Classify(Poses.Make(yaw, pitch));
        Assert.Equal(PoseClassification.ClassKind.Elsewhere, result.Kind);
        Assert.Equal(0, result.Margin);
    }

    [Theory]
    [InlineData(-20.0, 0.0)]
    [InlineData(30.0, 0.0)]
    [InlineData(5.0, -15.0)]
    [InlineData(5.0, 15.0)]
    public void PosesOnTheBoxEdgeAreStillDisplays(double yaw, double pitch) =>
        Assert.Equal(PoseClassification.ClassKind.Display, Classify(Poses.Make(yaw, pitch)).Kind);

    [Fact]
    public void ElsewhereKeepsBestRunnerUpAndDistances()
    {
        var result = Classify(Poses.Make(-25, 0));
        Assert.Equal(PoseClassification.ClassKind.Elsewhere, result.Kind);
        Assert.Equal(D1, result.Best);
        Assert.Equal(D2, result.RunnerUp);
        Assert.Equal(0, result.Margin);
        Assert.Equal(12.5, result.Distances[D1]);
        Assert.Equal(17.5, result.Distances[D2]);
    }

    [Fact]
    public void TheElsewhereBoxFollowsTheSettingsMargins()
    {
        var tight = Settings with { ElsewhereYawMargin = 5, ElsewherePitchMargin = 2 };
        Assert.Equal(PoseClassification.ClassKind.Elsewhere, Classify(Poses.Make(16, 0), settings: tight).Kind);
        Assert.Equal(PoseClassification.ClassKind.Display, Classify(Poses.Make(15, 0), settings: tight).Kind);
        Assert.Equal(PoseClassification.ClassKind.Elsewhere, Classify(Poses.Make(5, 3), settings: tight).Kind);
        Assert.Equal(PoseClassification.ClassKind.Display, Classify(Poses.Make(5, 2), settings: tight).Kind);
    }

    // No face

    [Fact]
    public void NoPoseIsNoFace()
    {
        Assert.Equal(PoseClassification.NoFace, Classify(null));
        Assert.Null(Classify(null).Best);
        Assert.Empty(Classify(null).Distances);
    }

    [Fact]
    public void ConfidenceBelowTheMinimumIsNoFace()
    {
        Assert.Equal(PoseClassification.NoFace, Classify(Poses.Make(0, 0, confidence: 0.49)));
        Assert.Equal(PoseClassification.ClassKind.Display, Classify(Poses.Make(0, 0, confidence: 0.5)).Kind);
    }

    [Fact]
    public void TheMinimumConfidenceComesFromTheSettings()
    {
        var strict = Settings with { MinFaceConfidence = 0.9 };
        Assert.Equal(PoseClassification.NoFace, Classify(Poses.Make(0, 0, confidence: 0.8), settings: strict));
        Assert.Equal(PoseClassification.ClassKind.Display, Classify(Poses.Make(0, 0, confidence: 0.8)).Kind);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(3.0, null)]
    [InlineData(null, 4.0)]
    public void ALandmarkProfileTreatsMissingLandmarksAsNoFace(double? landmarkYaw, double? landmarkPitch)
    {
        var landmarkProfile = Profile(PoseFeatureSet.LandmarkAngles);
        var pose = Poses.Make(0, 0, landmarkYaw: landmarkYaw, landmarkPitch: landmarkPitch);
        Assert.Equal(PoseClassification.NoFace, Classify(pose, landmarkProfile));
    }

    [Fact]
    public void ALandmarkProfileClassifiesOnLandmarkAnglesNotVisionAngles()
    {
        var landmarkProfile = Profile(PoseFeatureSet.LandmarkAngles);
        // Vision angles are far outside, but the landmark angles are the ones used.
        var pose = Poses.Make(99, 99, landmarkYaw: 2, landmarkPitch: 4);
        var result = Classify(pose, landmarkProfile);
        Assert.Equal(Classify(Poses.Make(2, 4)), result);
        Assert.Equal(PoseClassification.ClassKind.Display, result.Kind);
        Assert.Equal(D1, result.Best);
    }

    [Fact]
    public void AVisionProfileIgnoresLandmarkAngles()
    {
        var pose = Poses.Make(2, 4, landmarkYaw: 99, landmarkPitch: 99);
        Assert.Equal(Classify(Poses.Make(2, 4)), Classify(pose));
    }

    // Profiles the fitter never produces

    [Fact]
    public void AProfileWithoutDisplaysAlwaysGivesNoFace()
    {
        var empty = Profile(displays: []);
        Assert.Equal(PoseClassification.NoFace, Classify(Poses.Make(0, 0), empty));
    }

    [Fact]
    public void ASingleDisplayHasNoRunnerUpAndNeverAConfidentMargin()
    {
        var single = Profile(displays: [(D1, 0, 0)]);
        var result = Classify(Poses.Make(1, 0), single);
        Assert.Equal(PoseClassification.ClassKind.Display, result.Kind);
        Assert.Equal(D1, result.Best);
        Assert.Null(result.RunnerUp);
        Assert.Equal(0, result.Margin);
        Assert.Single(result.Distances);
    }
}
