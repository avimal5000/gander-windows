namespace Sway.Core;

/// <summary>
/// Where the head points, in degrees. Sign and zero conventions are fixed in CONTRACT.md, so every pose source
/// (the live camera, a replayed trace, a test) means the same thing.
/// </summary>
public readonly record struct HeadPose(double Yaw, double Pitch, double Roll);
