namespace Sway.Core.Platform;

/// <param name="CameraDeviceId">Null means the default (built-in, else first).</param>
/// <param name="FrameRate">Requested analysis rate in frames per second.</param>
/// <param name="IncludeLandmarks">Also compute the landmark angles (gate test only; costs CPU).</param>
public sealed record PoseSourceConfiguration(
    string? CameraDeviceId = null,
    double FrameRate = SwayPolicy.DefaultFrameRate,
    bool IncludeLandmarks = false);

/// <summary>Something a pose source reports: a pose sample (null pose means no face) or a capture status change.</summary>
public abstract record PoseSourceEvent
{
    public sealed record Sample(TimedPose Pose) : PoseSourceEvent;

    public sealed record Status(CaptureStatus Value) : PoseSourceEvent;
}

/// <summary>
/// Head pose from the live camera, a replayed trace, or a test script. Started and stopped by the tracking controller;
/// the camera runs only between <see cref="StartAsync"/> and <see cref="StopAsync"/>.
/// </summary>
public interface IPoseSource
{
    /// <summary>Starts capture and returns the event stream. The stream ends when <see cref="StopAsync"/> completes.</summary>
    /// <exception cref="InvalidOperationException">Already started.</exception>
    Task<IAsyncEnumerable<PoseSourceEvent>> StartAsync(PoseSourceConfiguration configuration, CancellationToken cancellationToken = default);

    Task StopAsync();

    /// <summary>How much analysis the controller wants right now (full, adaptive, reduced). Sources that cannot vary ignore it.</summary>
    void SetDemand(Sway.Core.Runtime.PoseDemand demand);
}
