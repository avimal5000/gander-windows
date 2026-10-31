using System.Diagnostics;
using Sway.Core;
using Sway.Core.Pose;
using Windows.Graphics.Imaging;
using Windows.Media.FaceAnalysis;

namespace Sway.Platform.Windows.Pose;

/// <summary>The outcome of one frame.</summary>
/// <param name="Pose">Null when no usable face was found.</param>
/// <param name="Milliseconds">Time spent on this frame (face finding, model, pose).</param>
internal sealed record PoseFrame(HeadPose? Pose, double Presence, double Milliseconds, FaceSource Source);

/// <summary>
/// Frame to head pose. The system FaceDetector finds the first box (on a Gray8 copy); after that the box follows the
/// previous frame's landmarks, enlarged 1.5x like MediaPipe does. When the face is lost the detector runs again, and if
/// it finds nothing (a face turned far to one side often is missed) the model is tried on a few candidate boxes per
/// frame (<see cref="FaceSearchPlan"/>), rotating across frames.
/// </summary>
internal sealed class PosePipeline : IDisposable
{
    /// <summary>The model's face-presence score a result must reach to count as a face (tracked, detected or fallback).</summary>
    internal const double PresenceThreshold = 0.5;

    private const double DetectorBoxScale = 1.6; // untested guess: the Windows detector box is tighter than MediaPipe's
    private const double TrackingBoxScale = 1.5;

    private readonly LandmarkModel _model;
    private readonly FaceDetector? _detector;
    private readonly FaceSearchPlan _plan = new();
    private SquareBox? _box;
    private SquareBox? _lastBox;

    private PosePipeline(LandmarkModel model, FaceDetector? detector)
    {
        _model = model;
        _detector = detector;
    }

    /// <summary>True when the system face detector is available. When not, a box in the middle of the frame is tried instead.</summary>
    internal bool HasDetector => _detector is not null;

    /// <summary>The model's input/output description, for the report.</summary>
    internal string ModelDescription => _model.Description;

    /// <summary>Opens the pipeline from the model at <paramref name="modelPath"/> (default: next to the app), or returns null with the reason in <paramref name="failure"/>. <paramref name="log"/> receives diagnostic lines.</summary>
    internal static PosePipeline? TryCreate(Action<string> log, out string failure, string? modelPath = null)
    {
        failure = string.Empty;
        var model = LandmarkModel.TryOpen(modelPath ?? LandmarkModel.DefaultPath, out var message);
        log(message);
        if (model is null)
        {
            failure = message;
            return null;
        }

        FaceDetector? detector = null;
        if (!FaceDetector.IsSupported)
        {
            log("Windows.Media.FaceAnalysis.FaceDetector is NOT supported on this PC. Falling back to a box in the");
            log("middle of the frame; keep your face centered when a run starts.");
        }
        else
        {
            try
            {
                detector = FaceDetector.CreateAsync().AsTask().GetAwaiter().GetResult();
                log($"FaceDetector ready (Gray8 supported: {FaceDetector.IsBitmapPixelFormatSupported(BitmapPixelFormat.Gray8)}).");
            }
            catch (Exception exception)
            {
                log($"FaceDetector could not start ({exception.Message}). Falling back to a centered box.");
            }
        }

        return new PosePipeline(model, detector);
    }

    /// <summary>Forgets the tracked box and the last known face, as if the camera had just started.</summary>
    internal void Reset()
    {
        _box = null;
        _lastBox = null;
        _plan.Reset();
    }

    internal PoseFrame Process(CameraFrame frame)
    {
        var timer = Stopwatch.StartNew();
        var presence = 0.0;

        // 1. Tracking: one inference on the previous frame's box.
        if (_box is { } tracked)
        {
            var result = _model.Run(frame, tracked);
            presence = result?.Presence ?? 0;
            if (TryAccept(result, frame, out var pose))
            {
                return new PoseFrame(pose, presence, timer.Elapsed.TotalMilliseconds, FaceSource.Tracked);
            }

            _box = null; // lost
        }

        // 2. The system detector (or the centered box when there is no detector).
        var detected = Detect(frame);
        if (detected is { } fresh)
        {
            var result = _model.Run(frame, fresh);
            presence = Math.Max(presence, result?.Presence ?? 0);
            if (TryAccept(result, frame, out var pose))
            {
                return new PoseFrame(pose, presence, timer.Elapsed.TotalMilliseconds, FaceSource.Detector);
            }
        }

        // 3. Fallback: the landmark model on a few candidate boxes, rotating across frames.
        foreach (var candidate in _plan.Next(_lastBox, frame.Width, frame.Height, FaceSearchPlan.DefaultAttemptsPerFrame))
        {
            var result = _model.Run(frame, candidate);
            presence = Math.Max(presence, result?.Presence ?? 0);
            if (TryAccept(result, frame, out var pose))
            {
                return new PoseFrame(pose, presence, timer.Elapsed.TotalMilliseconds, FaceSource.Fallback);
            }
        }

        return new PoseFrame(null, presence, timer.Elapsed.TotalMilliseconds, FaceSource.NotFound);
    }

    /// <summary>Accepts a model result whose presence passes the threshold and whose landmarks give a pose; updates the tracked box.</summary>
    private bool TryAccept(LandmarkResult? result, CameraFrame frame, out HeadPose pose)
    {
        pose = default;
        if (result is null || result.Presence < PresenceThreshold || PoseGeometry.TryCompute(result.Landmarks) is not { } computed)
        {
            return false;
        }

        pose = computed with { FaceBounds = FaceBoundsMapper.FromLandmarks(result.Landmarks, frame.Width, frame.Height) };
        _box = SquareBox.FromLandmarks(result.Landmarks, TrackingBoxScale);
        if (_box is not null)
        {
            _lastBox = _box;
        }

        _plan.Reset();
        return true;
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
