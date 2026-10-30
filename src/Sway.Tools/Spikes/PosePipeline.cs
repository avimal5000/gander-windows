using System.Diagnostics;
using Sway.Core;
using Windows.Graphics.Imaging;
using Windows.Media.FaceAnalysis;

namespace Sway.Tools.Spikes;

/// <summary>The outcome of one frame.</summary>
/// <param name="Pose">Null when no usable face was found.</param>
/// <param name="Milliseconds">Time spent on this frame (face finding, model, pose).</param>
internal sealed record PoseFrame(HeadPose? Pose, double Presence, double Milliseconds, bool Detected);

/// <summary>
/// Frame to head pose. The system FaceDetector finds the first box (on a Gray8 copy); after that the box follows the
/// previous frame's landmarks, enlarged 1.5x like MediaPipe does. When the model's face-presence score drops or the
/// landmarks are unusable, the box is thrown away and the detector runs again.
/// </summary>
internal sealed class PosePipeline : IDisposable
{
    private const double PresenceThreshold = 0.5;
    private const double DetectorBoxScale = 1.6; // untested guess: the Windows detector box is tighter than MediaPipe's
    private const double TrackingBoxScale = 1.5;

    private readonly LandmarkModel _model;
    private readonly FaceDetector? _detector;
    private SquareBox? _box;

    private PosePipeline(LandmarkModel model, FaceDetector? detector)
    {
        _model = model;
        _detector = detector;
    }

    /// <summary>True when the system face detector is available. When not, a box in the middle of the frame is tried instead.</summary>
    internal bool HasDetector => _detector is not null;

    /// <summary>The model's input/output description, for the report.</summary>
    internal string ModelDescription => _model.Description;

    /// <summary>Opens the pipeline, or returns null and a message.</summary>
    internal static PosePipeline? TryCreate(Report report)
    {
        var model = LandmarkModel.TryOpen(LandmarkModel.DefaultPath, out var message);
        report.Line(message);
        if (model is null)
        {
            return null;
        }

        FaceDetector? detector = null;
        if (!FaceDetector.IsSupported)
        {
            report.Line("Windows.Media.FaceAnalysis.FaceDetector is NOT supported on this PC. Falling back to a box in the");
            report.Line("middle of the frame; keep your face centered when a run starts.");
        }
        else
        {
            try
            {
                detector = FaceDetector.CreateAsync().AsTask().GetAwaiter().GetResult();
                report.Line($"FaceDetector ready (Gray8 supported: {FaceDetector.IsBitmapPixelFormatSupported(BitmapPixelFormat.Gray8)}).");
            }
            catch (Exception exception)
            {
                report.Line($"FaceDetector could not start ({exception.Message}). Falling back to a centered box.");
            }
        }

        return new PosePipeline(model, detector);
    }

    internal PoseFrame Process(CameraFrame frame)
    {
        var timer = Stopwatch.StartNew();
        var detectedNow = false;
        var presence = 0.0;

        // At most two tries: the tracked box, then (if that failed or there was none) a fresh detection.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (_box is null)
            {
                _box = Detect(frame);
                detectedNow = true;
                if (_box is null)
                {
                    break;
                }
            }

            var result = _model.Run(frame, _box.Value);
            presence = result?.Presence ?? 0;
            if (result is not null && result.Presence >= PresenceThreshold && PoseGeometry.TryCompute(result.Landmarks) is { } pose)
            {
                _box = SquareBox.FromLandmarks(result.Landmarks, TrackingBoxScale);
                return new PoseFrame(pose, presence, timer.Elapsed.TotalMilliseconds, detectedNow);
            }

            _box = null; // lost: detect again
            if (detectedNow)
            {
                break; // that box was fresh, so another detection this frame would find the same thing
            }
        }

        return new PoseFrame(null, presence, timer.Elapsed.TotalMilliseconds, detectedNow);
    }

    private SquareBox? Detect(CameraFrame frame)
    {
        if (_detector is null)
        {
            var side = Math.Min(frame.Width, frame.Height) * 0.7;
            return new SquareBox(frame.Width / 2.0, frame.Height / 2.0, side);
        }

        var gray = new byte[frame.Width * frame.Height];
        for (var i = 0; i < gray.Length; i++)
        {
            var at = i * 4; // BGRA; integer luma
            gray[i] = (byte)(((frame.Pixels[at] * 29) + (frame.Pixels[at + 1] * 150) + (frame.Pixels[at + 2] * 77)) >> 8);
        }

        using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(
            System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.AsBuffer(gray),
            BitmapPixelFormat.Gray8,
            frame.Width,
            frame.Height);
        var faces = _detector.DetectFacesAsync(bitmap).AsTask().GetAwaiter().GetResult();
        var largest = faces.OrderByDescending(face => (long)face.FaceBox.Width * face.FaceBox.Height).FirstOrDefault();
        return largest is null
            ? null
            : SquareBox.AroundRectangle(largest.FaceBox.X, largest.FaceBox.Y, largest.FaceBox.Width, largest.FaceBox.Height, DetectorBoxScale);
    }

    public void Dispose()
    {
        _model.Dispose();
    }
}
