namespace Gander.Core.Runtime;

/// <summary>
/// How long to wait before the next try at a camera that is not working (SPEC D47). Two independent sequences, each
/// growing with every failure and ending in a delay that repeats forever:
/// <list type="bullet">
/// <item>failing to find or open the device: 1 s, 2 s, 5 s, then 10 s;</item>
/// <item>rebuilding a stalled camera (the session runs but no frames arrive): 10 s, 30 s, then 60 s.</item>
/// </list>
/// Frames flowing again end the trouble, so <see cref="FramesFlowed"/> starts both sequences over.
/// </summary>
public sealed class RetryBackoff : IEquatable<RetryBackoff>
{
    public static readonly IReadOnlyList<double> OpenFailureDelays = new double[] { 1, 2, 5, 10 };

    public static readonly IReadOnlyList<double> StallRebuildDelays = new double[] { 10, 30, 60 };

    private int _openFailures;
    private int _stallWaits;

    /// <summary>The device could not be found or opened. Returns the wait before the next try.</summary>
    public double NextOpenFailureDelay()
    {
        var delay = DelayAt(OpenFailureDelays, _openFailures);
        _openFailures = Math.Min(_openFailures + 1, OpenFailureDelays.Count);
        return delay;
    }

    /// <summary>A stall began, or a rebuild did not bring frames back. Returns the wait before rebuilding again.</summary>
    public double NextStallRebuildDelay()
    {
        var delay = DelayAt(StallRebuildDelays, _stallWaits);
        _stallWaits = Math.Min(_stallWaits + 1, StallRebuildDelays.Count);
        return delay;
    }

    /// <summary>Frames are flowing: both sequences start over.</summary>
    public void FramesFlowed()
    {
        _openFailures = 0;
        _stallWaits = 0;
    }

    public bool Equals(RetryBackoff? other) =>
        other is not null && _openFailures == other._openFailures && _stallWaits == other._stallWaits;

    public override bool Equals(object? obj) => Equals(obj as RetryBackoff);

    public override int GetHashCode() => HashCode.Combine(_openFailures, _stallWaits);

    private static double DelayAt(IReadOnlyList<double> delays, int index) => delays[Math.Min(index, delays.Count - 1)];
}
