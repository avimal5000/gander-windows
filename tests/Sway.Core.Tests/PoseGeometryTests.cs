namespace Sway.Core.Tests;

public class PoseGeometryTests
{
    /// <summary>
    /// A synthetic face in head coordinates (x toward the person's left = image right at zero pose, y down, z away from
    /// the camera at zero pose), rotated by the given pose using the same convention PoseGeometry documents.
    /// </summary>
    private static Landmark[] Face(double yaw, double pitch, double roll)
    {
        var landmarks = new Landmark[478];
        landmarks[PoseGeometry.RightEyeOuter] = new Landmark(-45, 0, 0);
        landmarks[PoseGeometry.LeftEyeOuter] = new Landmark(45, 0, 0);
        landmarks[PoseGeometry.NoseBridge] = new Landmark(0, 0, -5);
        landmarks[PoseGeometry.NoseTip] = new Landmark(0, 40, -35); // in front of the eye line (toward the camera)
        landmarks[PoseGeometry.Chin] = new Landmark(0, 100, -5);
        for (var i = 0; i < landmarks.Length; i++)
        {
            landmarks[i] = Rotate(landmarks[i], yaw, pitch, roll);
        }

        return landmarks;
    }

    // Rotation = Ry(yaw) * Rx(-pitch) * Rz(-roll): roll about the facing axis first, then pitch, then yaw.
    private static Landmark Rotate(Landmark p, double yawDegrees, double pitchDegrees, double rollDegrees)
    {
        var (x, y, z) = (p.X, p.Y, p.Z);
        var g = -rollDegrees * Math.PI / 180;
        (x, y) = ((x * Math.Cos(g)) - (y * Math.Sin(g)), (x * Math.Sin(g)) + (y * Math.Cos(g)));
        var b = -pitchDegrees * Math.PI / 180;
        (y, z) = ((y * Math.Cos(b)) - (z * Math.Sin(b)), (y * Math.Sin(b)) + (z * Math.Cos(b)));
        var f = yawDegrees * Math.PI / 180;
        (x, z) = ((x * Math.Cos(f)) + (z * Math.Sin(f)), (-x * Math.Sin(f)) + (z * Math.Cos(f)));
        return new Landmark(x, y, z);
    }

    [Fact]
    public void FacingTheCameraIsZero()
    {
        var pose = PoseGeometry.TryCompute(Face(0, 0, 0));
        Assert.NotNull(pose);
        Assert.Equal(0, pose.Value.Yaw, 6);
        Assert.Equal(0, pose.Value.Pitch, 6);
        Assert.Equal(0, pose.Value.Roll, 6);
    }

    [Theory]
    [InlineData(25, 0, 0)]
    [InlineData(-30, 0, 0)]
    [InlineData(0, 15, 0)]
    [InlineData(0, -20, 0)]
    [InlineData(0, 0, 12)]
    [InlineData(0, 0, -18)]
    [InlineData(20, 10, 5)]
    [InlineData(-35, -12, 8)]
    [InlineData(40, 25, -15)]
    public void RecoversKnownAngles(double yaw, double pitch, double roll)
    {
        var pose = PoseGeometry.TryCompute(Face(yaw, pitch, roll));
        Assert.NotNull(pose);
        Assert.Equal(yaw, pose.Value.Yaw, 5);
        Assert.Equal(pitch, pose.Value.Pitch, 5);
        Assert.Equal(roll, pose.Value.Roll, 5);
    }

    [Fact]
    public void TurningTowardTheUsersRightGivesPositiveYawAndMovesTheNoseToImageLeft()
    {
        var turned = Face(20, 0, 0);
        var neutral = Face(0, 0, 0);
        Assert.True(turned[PoseGeometry.NoseTip].X < neutral[PoseGeometry.NoseTip].X); // image-left is the user's right
        Assert.True(PoseGeometry.TryCompute(turned)!.Value.Yaw > 0);
    }

    [Fact]
    public void LookingUpIsPositivePitchAndRightEarToShoulderIsPositiveRoll()
    {
        var up = Face(0, 15, 0);
        Assert.True(up[PoseGeometry.NoseTip].Y < Face(0, 0, 0)[PoseGeometry.NoseTip].Y); // up is smaller y
        Assert.True(PoseGeometry.TryCompute(up)!.Value.Pitch > 0);

        var tilted = Face(0, 0, 15);
        // The right ear side is image-left (the eye 33 side); it drops (larger y) for positive roll.
        Assert.True(tilted[PoseGeometry.RightEyeOuter].Y > tilted[PoseGeometry.LeftEyeOuter].Y);
        Assert.True(PoseGeometry.TryCompute(tilted)!.Value.Roll > 0);
    }

    [Fact]
    public void ScaleDoesNotChangeTheAngles()
    {
        var big = Face(18, 7, -4).Select(p => new Landmark(p.X * 3, p.Y * 3, p.Z * 3)).ToArray();
        var pose = PoseGeometry.TryCompute(big)!.Value;
        Assert.Equal(18, pose.Yaw, 5);
        Assert.Equal(7, pose.Pitch, 5);
        Assert.Equal(-4, pose.Roll, 5);
    }

    [Fact]
    public void Accepts468Landmarks()
    {
        var pose = PoseGeometry.TryCompute(Face(10, 0, 0).Take(468).ToArray());
        Assert.NotNull(pose);
    }

    [Fact]
    public void RejectsTooFewLandmarks()
    {
        Assert.Null(PoseGeometry.TryCompute(new Landmark[100]));
    }

    [Fact]
    public void RejectsAllZeroLandmarks()
    {
        Assert.Null(PoseGeometry.TryCompute(new Landmark[478]));
    }

    [Fact]
    public void RejectsNonFiniteLandmarks()
    {
        var face = Face(0, 0, 0);
        face[PoseGeometry.Chin] = new Landmark(double.NaN, 0, 0);
        Assert.Null(PoseGeometry.TryCompute(face));
    }

    [Fact]
    public void RejectsANoseBehindTheEyeLine()
    {
        var face = Face(0, 0, 0);
        face[PoseGeometry.NoseTip] = new Landmark(0, 40, 35);
        Assert.Null(PoseGeometry.TryCompute(face));
    }
}
