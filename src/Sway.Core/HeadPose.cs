namespace Sway.Core;

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
public readonly record struct HeadPose(
    double Yaw,
    double Pitch,
    double Roll,
    double FaceConfidence = 1.0,
    double? LandmarkYaw = null,
    double? LandmarkPitch = null);
