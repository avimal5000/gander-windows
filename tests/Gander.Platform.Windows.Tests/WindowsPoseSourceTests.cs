using Gander.Core.Platform;
using Gander.Platform.Windows;

namespace Gander.Platform.Windows.Tests;

public class WindowsPoseSourceTests
{
    private sealed class NoCameras : ICameraCatalog
    {
        public event Action? DevicesChanged
        {
            add { }
            remove { }
        }

        public CameraAuthorization Authorization() => CameraAuthorization.Authorized;

        public IReadOnlyList<CameraDevice> AvailableDevices() => [];

        public CameraDevice? Resolve(string? deviceId) => null;
    }

    private static async Task<List<CaptureStatus>> ReadStatusesAsync(IAsyncEnumerable<PoseSourceEvent> stream)
    {
        var statuses = new List<CaptureStatus>();
        await foreach (var item in stream.WithCancellation(new CancellationTokenSource(TimeSpan.FromSeconds(20)).Token))
        {
            if (item is PoseSourceEvent.Status status)
            {
                statuses.Add(status.Value);
            }
        }

        return statuses;
    }

    [Fact]
    public async Task MissingModelFailsWithConfigurationFailedAndStopCompletesTheStream()
    {
        using var source = new WindowsPoseSource(new NoCameras(), Path.Combine(Path.GetTempPath(), "no-such-model.onnx"));
        var stream = await source.StartAsync(new PoseSourceConfiguration());
        var reading = ReadStatusesAsync(stream);

        await Task.Delay(500);
        await source.StopAsync();
        var statuses = await reading;

        Assert.IsType<CaptureStatus.Starting>(statuses[0]);
        var failed = Assert.IsType<CaptureStatus.Failed>(statuses[1]);
        Assert.Equal(CameraErrorKind.ConfigurationFailed, failed.Error.Kind);
        Assert.Contains("missing", failed.Error.Detail);
        Assert.IsType<CaptureStatus.Stopped>(statuses[^1]);
    }

    [Fact]
    public async Task StartingTwiceThrows()
    {
        using var source = new WindowsPoseSource(new NoCameras(), "missing.onnx");
        await source.StartAsync(new PoseSourceConfiguration());
        await Assert.ThrowsAsync<InvalidOperationException>(() => source.StartAsync(new PoseSourceConfiguration()));
        await source.StopAsync();
    }
}
