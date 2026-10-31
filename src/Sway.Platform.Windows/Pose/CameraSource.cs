using System.Buffers;
using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using Sway.Core.Platform;
using Sway.Core.Runtime;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.MediaProperties;

namespace Sway.Platform.Windows.Pose;

/// <summary>One camera frame as BGRA bytes. Dispose it to give the buffer back; frames are never saved anywhere.</summary>
internal sealed class CameraFrame : IDisposable
{
    internal CameraFrame(byte[] pixels, int width, int height, double timeSeconds)
    {
        Pixels = pixels;
        Width = width;
        Height = height;
        TimeSeconds = timeSeconds;
    }

    /// <summary>Rented buffer, at least Width * Height * 4 bytes (it may be longer).</summary>
    internal byte[] Pixels { get; }

    internal int Width { get; }

    internal int Height { get; }

    /// <summary>Seconds since the camera started.</summary>
    internal double TimeSeconds { get; }

    public void Dispose() => ArrayPool<byte>.Shared.Return(Pixels);
}

/// <summary>
/// Camera capture through Windows.Media.Capture: a MediaFrameReader on the color source group of the chosen device
/// (by its Windows.Devices.Enumeration id; with no id, a Logitech C922 if present, else the first), in a format near
/// 640x480. Only the newest frame is kept; an older one that nobody took is dropped. Frames live in memory only.
/// </summary>
internal sealed class CameraSource : IDisposable
{
    private const int TargetWidth = 640;
    private const int TargetHeight = 480;

    private readonly object _gate = new();
    private readonly Stopwatch _stopwatch = new();
    private readonly Func<double> _clock;
    private readonly Func<double, bool>? _accept;
    private readonly SemaphoreSlim _signal = new(0, 1);
    private long _lastArrivalTicks = long.MinValue;
    private MediaCapture? _capture;
    private MediaFrameReader? _reader;
    private CameraFrame? _latest;
    private long _sequence;
    private int _busy;

    /// <param name="clock">Seconds on a monotonic clock for frame timestamps; null means seconds since this source started.</param>
    /// <param name="accept">
    /// Asked for every arriving frame before it is converted; false drops the frame cheaply (the frame-rate gate).
    /// Called on the capture callback thread.
    /// </param>
    internal CameraSource(Func<double>? clock = null, Func<double, bool>? accept = null)
    {
        _clock = clock ?? (() => _stopwatch.Elapsed.TotalSeconds);
        _accept = accept;
    }

    /// <summary>Raised (on any thread) when the capture session reports it failed, for example the camera was unplugged.</summary>
    internal event Action<string>? CaptureFailed;

    /// <summary>The clock time of the newest frame that arrived, converted or not; null before the first one.</summary>
    internal double? LastArrivalSeconds
    {
        get
        {
            var ticks = Interlocked.Read(ref _lastArrivalTicks);
            return ticks == long.MinValue ? null : BitConverter.Int64BitsToDouble(ticks);
        }
    }

    /// <summary>Waits until a converted frame is available (or the timeout / cancellation). True when one may be waiting.</summary>
    internal bool WaitForFrame(int millisecondsTimeout, CancellationToken cancellationToken)
    {
        try
        {
            return _signal.Wait(millisecondsTimeout, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>The system's id of the camera being read, once started.</summary>
    internal string? DeviceId { get; private set; }

    /// <summary>The camera's name and chosen format, for the report.</summary>
    internal string Description { get; private set; } = "(not started)";

    /// <summary>Starts the camera. Returns null on success, otherwise a message a person can act on.</summary>
    internal string? Start(string? deviceId = null) => OpenAsync(deviceId).GetAwaiter().GetResult().Detail;

    /// <summary>Starts the camera without throwing. Kind is null on success.</summary>
    internal async Task<CameraOpenResult> OpenAsync(string? deviceId = null)
    {
        try
        {
            var problem = await StartAsync(deviceId).ConfigureAwait(false);
            return problem ?? CameraOpenResult.Success;
        }
        catch (UnauthorizedAccessException)
        {
            return new CameraOpenResult(CameraErrorKind.NotAuthorized, "Camera access is denied. Open Settings, Privacy and security, Camera, and allow desktop apps to use the camera.");
        }
        catch (Exception exception)
        {
            return new CameraOpenResult(CameraErrorKind.DeviceUnavailable, $"Could not start the camera: {exception.Message}");
        }
    }

    /// <summary>The newest frame not yet taken, or null. The caller disposes it.</summary>
    internal CameraFrame? TryTake(ref long lastSequence)
    {
        lock (_gate)
        {
            if (_latest is null || _sequence == lastSequence)
            {
                return null;
            }

            lastSequence = _sequence;
            var frame = _latest;
            _latest = null;
            return frame;
        }
    }

    public void Dispose()
    {
        try
        {
            if (_capture is not null)
            {
                _capture.Failed -= OnCaptureFailed;
            }

            if (_reader is not null)
            {
                _reader.FrameArrived -= OnFrameArrived;
                // A driver that stalls must not hang the caller (quit): wait a few seconds, then release the camera anyway
                // by disposing the capture below.
                var stopped = _reader.StopAsync().AsTask();
                if (BoundedWait.TryWait(stopped, BoundedWait.CameraStopTimeout))
                {
                    _reader.Dispose();
                }
            }
        }
        catch (Exception)
        {
            // Shutting down: nothing useful to do if the reader is already gone.
        }

        _reader = null;
        _capture?.Dispose();
        _capture = null;
        lock (_gate)
        {
            _latest?.Dispose();
            _latest = null;
        }
    }

    private async Task<CameraOpenResult?> StartAsync(string? deviceId)
    {
        var groups = await MediaFrameSourceGroup.FindAllAsync();
        var candidates = groups
            .Select(group => (Group: group, Info: group.SourceInfos.FirstOrDefault(info => info.SourceKind == MediaFrameSourceKind.Color)))
            .Where(candidate => candidate.Info is not null)
            .ToList();
        if (candidates.Count == 0)
        {
            return new CameraOpenResult(CameraErrorKind.NoDevice, "No camera with color video was found. Plug in the webcam and check that no other app has it open.");
        }

        (MediaFrameSourceGroup Group, MediaFrameSourceInfo? Info) chosen;
        if (!string.IsNullOrEmpty(deviceId))
        {
            chosen = candidates.FirstOrDefault(candidate => string.Equals(candidate.Info?.DeviceInformation?.Id, deviceId, StringComparison.OrdinalIgnoreCase));
            if (chosen.Group is null)
            {
                return new CameraOpenResult(CameraErrorKind.DeviceUnavailable, "The chosen camera is not available. It may have been unplugged.");
            }
        }
        else
        {
            chosen = candidates.FirstOrDefault(candidate => candidate.Group.DisplayName.Contains("C922", StringComparison.OrdinalIgnoreCase));
            if (chosen.Group is null)
            {
                chosen = candidates[0];
            }
        }

        DeviceId = chosen.Info?.DeviceInformation?.Id;

        _capture = new MediaCapture();
        await _capture.InitializeAsync(new MediaCaptureInitializationSettings
        {
            SourceGroup = chosen.Group,
            SharingMode = MediaCaptureSharingMode.ExclusiveControl, // needed to choose the format
            MemoryPreference = MediaCaptureMemoryPreference.Cpu, // frames as SoftwareBitmap, not GPU surfaces
            StreamingCaptureMode = StreamingCaptureMode.Video,
        });

        _capture.Failed += OnCaptureFailed;
        var source = _capture.FrameSources.Values.FirstOrDefault(candidate => candidate.Info.SourceKind == MediaFrameSourceKind.Color);
        if (source is null)
        {
            return new CameraOpenResult(CameraErrorKind.ConfigurationFailed, "The camera has no color frame source.");
        }

        var format = ChooseFormat(source.SupportedFormats);
        var formatText = "(default format)";
        if (format is not null)
        {
            try
            {
                await source.SetFormatAsync(format);
                formatText = Describe(format);
            }
            catch (Exception exception)
            {
                formatText = $"(default format; could not set {Describe(format)}: {exception.Message})";
            }
        }

        _reader = await _capture.CreateFrameReaderAsync(source);
        _reader.AcquisitionMode = MediaFrameReaderAcquisitionMode.Realtime; // drop frames we are too slow for
        _reader.FrameArrived += OnFrameArrived;
        var status = await _reader.StartAsync();
        if (status != MediaFrameReaderStartStatus.Success)
        {
            return new CameraOpenResult(
                status == MediaFrameReaderStartStatus.OutputFormatNotSupported ? CameraErrorKind.ConfigurationFailed : CameraErrorKind.DeviceUnavailable,
                $"The camera would not start streaming ({status}). Another app may be using it.");
        }

        _stopwatch.Restart();
        Description = $"{chosen.Group.DisplayName}, {formatText}";
        return null;
    }

    /// <summary>Closest to 640x480, then close to 30 fps, then a plain uncompressed subtype over the others.</summary>
    private static MediaFrameFormat? ChooseFormat(IReadOnlyList<MediaFrameFormat> formats) => formats
        .Where(format => format.VideoFormat is { Width: > 0, Height: > 0 })
        .OrderBy(format => Math.Abs((int)format.VideoFormat.Width - TargetWidth) + Math.Abs((int)format.VideoFormat.Height - TargetHeight))
        .ThenBy(format => Math.Abs(FrameRate(format) - 30))
        .ThenBy(format => format.Subtype switch { "NV12" => 0, "YUY2" => 1, "MJPG" => 2, _ => 3 })
        .FirstOrDefault();

    private static double FrameRate(MediaFrameFormat format) =>
        format.FrameRate.Denominator == 0 ? 0 : (double)format.FrameRate.Numerator / format.FrameRate.Denominator;

    private static string Describe(MediaFrameFormat format) =>
        $"{format.VideoFormat.Width}x{format.VideoFormat.Height} {format.Subtype} {FrameRate(format):0.#} fps";

    private void OnCaptureFailed(MediaCapture sender, MediaCaptureFailedEventArgs args) =>
        CaptureFailed?.Invoke(args.Message);

    private void OnFrameArrived(MediaFrameReader sender, MediaFrameArrivedEventArgs args)
    {
        var arrival = _clock();
        Interlocked.Exchange(ref _lastArrivalTicks, BitConverter.DoubleToInt64Bits(arrival));
        if (_accept is not null && !_accept(arrival))
        {
            return; // not wanted at this rate: the frame is never converted or copied
        }

        // One conversion at a time; a frame that arrives during one is simply skipped.
        if (Interlocked.Exchange(ref _busy, 1) == 1)
        {
            return;
        }

        try
        {
            using var reference = sender.TryAcquireLatestFrame();
            using var source = reference?.VideoMediaFrame?.SoftwareBitmap is { } bitmap
                ? SoftwareBitmap.Convert(bitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied)
                : null;
            if (source is null)
            {
                return;
            }

            var width = source.PixelWidth;
            var height = source.PixelHeight;
            var pixels = ArrayPool<byte>.Shared.Rent(width * height * 4);
            source.CopyToBuffer(pixels.AsBuffer(0, width * height * 4));
            var frame = new CameraFrame(pixels, width, height, arrival);
            lock (_gate)
            {
                _latest?.Dispose(); // dropped without ever being processed
                _latest = frame;
                _sequence++;
            }

            try
            {
                _signal.Release();
            }
            catch (SemaphoreFullException)
            {
                // Already signalled: the loop will take the newest frame.
            }
        }
        catch (Exception)
        {
            // A bad frame is just a missing frame.
        }
        finally
        {
            Volatile.Write(ref _busy, 0);
        }
    }
}

/// <summary>The outcome of opening a camera: <see cref="Kind"/> null means it is streaming.</summary>
internal readonly record struct CameraOpenResult(CameraErrorKind? Kind, string? Detail)
{
    internal static CameraOpenResult Success => new(null, null);
}
