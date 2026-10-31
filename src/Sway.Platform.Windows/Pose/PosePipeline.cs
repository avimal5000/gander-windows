using System.Diagnostics;
using Sway.Core;
using Sway.Core.Pose;
using Windows.Graphics.Imaging;
using Windows.Media.FaceAnalysis;

namespace Sway.Platform.Windows.Pose;

/// <summary>The outcome of one frame.</summary>
/// <param name="Pose">Null when no usable face was found.</param>
/// <param name="Milliseconds">Time spent on this frame (face finding, model, pose).</param>
/// <param name="Via">Numbers-only note on how the face was found: tracked, detector, neighbourhood box k, search box k, or none.</param>
/// <param name="LowPresence">True when accepted below the cold-start score because the box followed a known face.</param>
internal sealed record PoseFrame(HeadPose? Pose, double Presence, double Milliseconds, FaceSource Source, string Via = "none", bool LowPresence = false);

/// <summary>
/// Frame to head pose. The system FaceDetector finds the first box (on a Gray8 copy); after that the box follows the
/// previous frame's landmarks, enlarged 1.5x like MediaPipe does. When the face is lost the detector runs again, and if
/// it finds nothing (a face turned far to one side often is missed) the model is tried on a few candidate boxes per
/// frame (<see cref="FaceSearchPlan"/>), rotating across frames.
/// </summary>
internal sealed class PosePipeline : IDisposable
{
    /// <summary>The model's face-presence score a result must reach to count as a face (tracked, detected or fallback).</summary>
    internal const double PresenceThreshold = FaceRecovery.ColdThreshold;

    private const double DetectorBoxScale = 1.6; // untested guess: the Windows detector box is tighter than MediaPipe's
    private const double TrackingBoxScale = 1.5;

    private readonly LandmarkModel _model;
    private readonly FaceDetector? _detector;
    private readonly FaceSearchPlan _plan = new();
    private byte[] _gray = [];
    private SquareBox? _box;
    private SquareBox? _lastBox;
    private (double X, double Y)? _drift;
    private int _framesLost;
    private double _bestPresenceWhileLost;
    private bool _wasFound;
    private bool _everFound;

    private const int LostLogEvery = 25;

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
        _drift = null;
        _framesLost = 0;
        _bestPresenceWhileLost = 0;
        _wasFound = false;
        _plan.Reset();
    }

    /// <summary>Receives one numbers-only line when the face state changes (tracked, lost, re-found). Null for none.</summary>
    internal Action<string>? StateLog { get; set; }

    internal PoseFrame Process(CameraFrame frame)
    {
        var timer = Stopwatch.StartNew();
        var presence = 0.0;
        var lostBefore = _framesLost;

        // 1. Tracking: one inference on the previous frame's box, at the lower score a followed face may have.
        if (_box is { } tracked)
        {
            var result = _model.Run(frame, tracked);
            presence = result?.Presence ?? 0;
            if (TryAccept(result, frame, FaceRecovery.TrackedThreshold, tracked, out var pose))
            {
                return Found(pose, presence, timer, FaceSource.Tracked, "tracked", lostBefore);
            }

            _box = null; // lost
        }

        // 2. Neighbourhood of the last face: the last box grown, then shifted, for the first frames after a loss.
        var attempt = 0;
        foreach (var candidate in FaceRecovery.NeighbourhoodForFrame(_lastBox, _drift, _framesLost + 1, frame.Width, frame.Height))
        {
            attempt++;
            var result = _model.Run(frame, candidate);
            presence = Math.Max(presence, result?.Presence ?? 0);
            if (TryAccept(result, frame, FaceRecovery.TrackedThreshold, _lastBox, out var pose))
            {
                return Found(pose, presence, timer, FaceSource.Fallback, $"neighbourhood box {attempt}", lostBefore);
            }
        }

        // 3. The system detector (or the centered box when there is no detector).
        var detected = Detect(frame);
        if (detected is { } fresh)
        {
            var result = _model.Run(frame, fresh);
            presence = Math.Max(presence, result?.Presence ?? 0);
            if (TryAccept(result, frame, FaceRecovery.ColdThreshold, null, out var pose))
            {
                return Found(pose, presence, timer, FaceSource.Detector, "detector", lostBefore);
            }
        }

        // 4. Fallback: the landmark model on a few candidate boxes, rotating across frames.
        var searchStart = _plan.Cursor;
        foreach (var candidate in _plan.Next(_lastBox, frame.Width, frame.Height, FaceSearchPlan.DefaultAttemptsPerFrame))
        {
            var result = _model.Run(frame, candidate);
            presence = Math.Max(presence, result?.Presence ?? 0);
            if (TryAccept(result, frame, FaceRecovery.ColdThreshold, null, out var pose))
            {
                return Found(pose, presence, timer, FaceSource.Fallback, $"search box {searchStart + 1}", lostBefore);
            }
        }

        _framesLost++;
        _bestPresenceWhileLost = Math.Max(_bestPresenceWhileLost, presence);
        if (_wasFound || _framesLost % LostLogEvery == 0)
        {
            var last = _lastBox is { } box ? $"cx {box.CenterX:0} cy {box.CenterY:0} side {box.Side:0}" : "none";
            StateLog?.Invoke($"Face lost (last box {last}; drift {_drift?.X ?? 0:0.0},{_drift?.Y ?? 0:0.0} px/frame; best presence {_bestPresenceWhileLost:0.00}; search attempt {_framesLost}; detector {(HasDetector ? "on" : "off")}).");
        }

        _wasFound = false;
        return new PoseFrame(null, presence, timer.Elapsed.TotalMilliseconds, FaceSource.NotFound, "none", false);
    }

    private PoseFrame Found(HeadPose pose, double presence, Stopwatch timer, FaceSource source, string via, int lostBefore)
    {
        var low = presence < FaceRecovery.ColdThreshold;
        if (!_wasFound)
        {
            StateLog?.Invoke(_everFound
                ? $"Face re-found via {via} (presence {presence:0.00}, after {lostBefore} lost frames)."
                : $"Face tracked (first found via {via}, presence {presence:0.00}).");
        }

        _wasFound = true;
        _everFound = true;
        _framesLost = 0;
        _bestPresenceWhileLost = 0;
        return new PoseFrame(pose, presence, timer.Elapsed.TotalMilliseconds, source, via, low);
    }

    /// <summary>Accepts a model result whose presence passes <paramref name="threshold"/> and whose landmarks give a pose (and, with <paramref name="reference"/>, a believable box); updates the tracked box.</summary>
    private bool TryAccept(LandmarkResult? result, CameraFrame frame, double threshold, SquareBox? reference, out HeadPose pose)
    {
        pose = default;
        if (result is null || result.Presence < threshold || PoseGeometry.TryCompute(result.Landmarks) is not { } computed)
        {
            return false;
        }

        var next = SquareBox.FromLandmarks(result.Landmarks, TrackingBoxScale);
        if (reference is { } known && result.Presence < FaceRecovery.ColdThreshold && (next is not { } nextBox || !FaceRecovery.Plausible(nextBox, known)))
        {
            return false; // a low-score fit is only trusted when it is the same face as before
        }

        pose = computed with { FaceBounds = FaceBoundsMapper.FromLandmarks(result.Landmarks, frame.Width, frame.Height) };
        if (next is { } accepted)
        {
            if (_lastBox is { } before && _wasFound)
            {
                _drift = (accepted.CenterX - before.CenterX, accepted.CenterY - before.CenterY);
            }

            _box = accepted;
            _lastBox = accepted;
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

        var length = frame.Width * frame.Height;
        if (_gray.Length != length)
        {
            _gray = new byte[length]; // only when the frame size changes; CreateCopyFromBuffer copies, so the buffer is reusable at once
        }

        var gray = _gray;
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
