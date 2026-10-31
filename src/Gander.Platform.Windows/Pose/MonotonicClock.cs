using System.Diagnostics;

namespace Gander.Platform.Windows.Pose;

/// <summary>Seconds on the system's monotonic high-resolution clock, the origin being arbitrary.</summary>
internal static class MonotonicClock
{
    internal static double Seconds => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
}
