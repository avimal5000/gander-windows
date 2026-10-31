namespace Gander.Core;

/// <summary>
/// Where the head points, in degrees. Sign and zero conventions are fixed in CONTRACT.md, so every pose source
/// (the live camera, a replayed trace, a test) means the same thing.
/// </summary>
/// <param name="Yaw">Degrees, positive toward the user's right.</param>
/// <param name="Pitch">Degrees, positive looking up.</param>
/// <param name="Roll">Degrees, positive right ear toward right shoulder.</param>
/// <param name="FaceConfidence">Face detection confidence, 0 to 1. Poses below the engine's minimum count as no face.
/// Sources that have no confidence leave it at 1.</param>
/// <param name="LandmarkYaw">Landmark-derived yaw proxy for the landmark feature set; null when unavailable.</param>
/// <param name="LandmarkPitch">Landmark-derived pitch proxy for the landmark feature set; null when unavailable.</param>
/// <param name="FaceBounds">The face box in the camera frame, for the camera check's framing advice; null when the source has none.</param>
public readonly record struct HeadPose(
    double Yaw,
    double Pitch,
    double Roll,
    double FaceConfidence = 1.0,
    double? LandmarkYaw = null,
    double? LandmarkPitch = null,
    FaceBounds? FaceBounds = null);

/// <summary>
/// Where the face sits in the camera frame, as fractions of the frame (0 to 1 on both axes, any origin: only sizes
/// and the distance from the middle matter). Used by the camera check's framing advice (SPEC F20). Never an image.
/// </summary>
/// <param name="X">Left edge, fraction of the frame width.</param>
/// <param name="Y">Top edge, fraction of the frame height.</param>
/// <param name="Width">Width, fraction of the frame width.</param>
/// <param name="Height">Height, fraction of the frame height.</param>
public readonly record struct FaceBounds(double X, double Y, double Width, double Height)
{
    /// <summary>Horizontal middle, fraction of the frame width.</summary>
    public double MidX => X + (Width / 2);

    /// <summary>Vertical middle, fraction of the frame height.</summary>
    public double MidY => Y + (Height / 2);
}
