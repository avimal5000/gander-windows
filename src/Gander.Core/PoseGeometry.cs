namespace Gander.Core;

/// <summary>A face landmark in the landmark model's crop pixel space (x right, y down, z away from the camera).</summary>
public readonly record struct Landmark(double X, double Y, double Z);

/// <summary>
/// Turns face landmarks (MediaPipe face-mesh indexing, 468 or 478 points) into a <see cref="HeadPose"/>.
///
/// Sign convention, in degrees, for an image that is NOT mirrored (the camera frame as the sensor delivers it):
/// <list type="bullet">
/// <item>Yaw is positive when the head turns toward the user's RIGHT. The user faces the camera, so the user's right
/// is the left side of the image: the nose moves toward image-left. Looking at a monitor on the right gives positive yaw.</item>
/// <item>Pitch is positive when the head looks UP.</item>
/// <item>Roll is positive when the right ear moves toward the right shoulder (the top of the head tips toward image-left).</item>
/// </list>
/// Zero means the face points straight at the camera with the eye line level. The camera is rarely where the user
/// looks "straight ahead", so calibration (not this class) handles the offset.
///
/// Method: build a head frame from five stable points and read the angles off its facing direction.
/// Lateral axis = outer eye corner 33 to outer eye corner 263. Vertical axis = nose bridge 168 to chin 152, made
/// perpendicular to the lateral axis. The facing direction is perpendicular to both, pointing out of the face.
/// Nose tip 1 is only used to reject frames where the nose is behind the eye line (a flipped or garbage fit).
/// MediaPipe z uses roughly the same scale as x, and smaller z is closer to the camera.
/// </summary>
public static class PoseGeometry
{
    /// <summary>Outer corner of the eye on the image-left side (the person's right eye).</summary>
    public const int RightEyeOuter = 33;

    /// <summary>Outer corner of the eye on the image-right side (the person's left eye).</summary>
    public const int LeftEyeOuter = 263;

    /// <summary>Nose tip.</summary>
    public const int NoseTip = 1;

    /// <summary>Chin.</summary>
    public const int Chin = 152;

    /// <summary>Nose bridge between the eyes.</summary>
    public const int NoseBridge = 168;

    /// <summary>The fewest landmarks a face mesh can have (468; 478 adds the irises).</summary>
    public const int MinimumLandmarks = 468;

    private const double RadiansToDegrees = 180.0 / Math.PI;

    /// <summary>The head pose for the landmarks, or null when they are too few, not finite, or degenerate.</summary>
    public static HeadPose? TryCompute(IReadOnlyList<Landmark> landmarks)
    {
        if (landmarks.Count < MinimumLandmarks)
        {
            return null;
        }

        var rightEye = landmarks[RightEyeOuter];
        var leftEye = landmarks[LeftEyeOuter];
        var nose = landmarks[NoseTip];
        var chin = landmarks[Chin];
        var bridge = landmarks[NoseBridge];

        var lateral = Normalize(Subtract(leftEye, rightEye));
        var down = Subtract(chin, bridge);
        if (lateral is not { } x || !IsFinite(down))
        {
            return null;
        }

        // Make the vertical axis perpendicular to the lateral one (Gram-Schmidt), then take the third axis.
        var along = Dot(down, x);
        var y = Normalize((down.X - (along * x.X), down.Y - (along * x.Y), down.Z - (along * x.Z)));
        if (y is not { } yAxis)
        {
            return null;
        }

        var intoHead = Cross(x, yAxis); // x right, y down, so this points away from the camera at zero pose
        var facing = (X: -intoHead.X, Y: -intoHead.Y, Z: -intoHead.Z);

        // The nose tip must be in front of the eye line, i.e. on the facing side of the head.
        var eyeMid = ((rightEye.X + leftEye.X) / 2, (rightEye.Y + leftEye.Y) / 2, (rightEye.Z + leftEye.Z) / 2);
        var noseOffset = (nose.X - eyeMid.Item1, nose.Y - eyeMid.Item2, nose.Z - eyeMid.Item3);
        if (!IsFinite(noseOffset) || Dot(noseOffset, facing) <= 0)
        {
            return null;
        }

        var yaw = Math.Atan2(-facing.X, -facing.Z);
        var pitch = Math.Atan2(-facing.Y, Math.Sqrt((facing.X * facing.X) + (facing.Z * facing.Z)));

        // Roll: how far the lateral axis is rotated about the facing direction from the level (zero roll) lateral axis.
        var levelLateral = Normalize(Cross(facing, (0, 1, 0)));
        if (levelLateral is not { } level)
        {
            return null; // looking straight up or down: roll is undefined
        }

        var levelUp = Cross(facing, level);
        var roll = Math.Atan2(Dot(x, levelUp), Dot(x, level));

        return new HeadPose(yaw * RadiansToDegrees, pitch * RadiansToDegrees, roll * RadiansToDegrees);
    }

    private static (double X, double Y, double Z) Subtract(Landmark a, Landmark b) => (a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    private static double Dot((double X, double Y, double Z) a, (double X, double Y, double Z) b) => (a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z);

    private static (double X, double Y, double Z) Cross((double X, double Y, double Z) a, (double X, double Y, double Z) b) =>
        ((a.Y * b.Z) - (a.Z * b.Y), (a.Z * b.X) - (a.X * b.Z), (a.X * b.Y) - (a.Y * b.X));

    private static bool IsFinite((double X, double Y, double Z) v) => double.IsFinite(v.X) && double.IsFinite(v.Y) && double.IsFinite(v.Z);

    private static (double X, double Y, double Z)? Normalize((double X, double Y, double Z) v)
    {
        if (!IsFinite(v))
        {
            return null;
        }

        var length = Math.Sqrt(Dot(v, v));
        return length < 1e-9 ? null : (v.X / length, v.Y / length, v.Z / length);
    }
}
