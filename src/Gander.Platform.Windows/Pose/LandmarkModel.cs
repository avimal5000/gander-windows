using Gander.Core;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Gander.Platform.Windows.Pose;

/// <summary>What one run of the landmark model returned.</summary>
/// <param name="Landmarks">In frame pixels (the crop box already undone).</param>
/// <param name="Presence">Face-presence probability, 0..1 (1 when the model has no such output).</param>
internal sealed record LandmarkResult(Landmark[] Landmarks, double Presence);

/// <summary>
/// The face-landmark model run with ONNX Runtime. Input and output names, shapes, layout and size are read from the
/// session's metadata at startup (NHWC or NCHW, any square size, RGB in [0,1]), so a different MediaPipe conversion
/// can be dropped in without code changes. Local inference only.
/// </summary>
internal sealed class LandmarkModel : IDisposable
{
    private static readonly object TelemetryGate = new();
    private static bool _telemetryOff;

    /// <summary>Intra-op threads for the session: small on purpose (see <see cref="TryOpen"/>).</summary>
    internal const int IntraOpThreads = 2;

    private readonly InferenceSession _session;
    private readonly string _inputName;
    private readonly ModelLayout _layout;
    private readonly string[] _outputNames;
    private readonly DenseTensor<float> _input;

    private LandmarkModel(InferenceSession session, string inputName, ModelLayout layout, string description)
    {
        _session = session;
        _inputName = inputName;
        _layout = layout;
        _outputNames = session.OutputNames.ToArray();
        Description = description;
        _input = new DenseTensor<float>(layout.ChannelsFirst ? new[] { 1, 3, layout.Size, layout.Size } : new[] { 1, layout.Size, layout.Size, 3 });
    }

    /// <summary>Human-readable input/output description for the report.</summary>
    internal string Description { get; }

    internal static string DefaultPath => Path.Combine(AppContext.BaseDirectory, "models", "face_landmark.onnx");

    /// <summary>The metadata of a model file as text, without needing a layout we understand. Used by "describe model".</summary>
    internal static string Describe(string path, out ModelLayout? layout)
    {
        layout = null;
        if (!File.Exists(path))
        {
            return $"Model file not found: {path}\nRun scripts/fetch-pose-model.sh in the repository and rebuild.";
        }

        var model = File.ReadAllBytes(path);
        EnsureTelemetryOff();
        using var session = new InferenceSession(model);
        return Describe(session, model.Length, path, out layout);
    }

    /// <summary>Describes an already open session, so the product opens the model once.</summary>
    private static string Describe(InferenceSession session, int modelLength, string path, out ModelLayout? layout)
    {
        layout = null;
        var lines = new List<string> { $"Model file: {path} ({modelLength:N0} bytes)" };
        var inputShape = new List<int>();
        foreach (var (name, meta) in session.InputMetadata)
        {
            lines.Add($"Input  '{name}': shape [{string.Join(", ", meta.Dimensions)}], type {meta.ElementDataType}");
            if (inputShape.Count == 0)
            {
                inputShape.AddRange(meta.Dimensions);
            }
        }

        var outputShapes = new List<IReadOnlyList<int>>();
        foreach (var name in session.OutputNames)
        {
            var meta = session.OutputMetadata[name];
            lines.Add($"Output '{name}': shape [{string.Join(", ", meta.Dimensions)}], type {meta.ElementDataType}");
            outputShapes.Add(meta.Dimensions);
        }

        layout = ModelLayout.Parse(inputShape, outputShapes);
        lines.Add(layout is null
            ? "Layout: NOT UNDERSTOOD (expected a 4-D RGB image input and an output of at least 468 x 3 numbers)."
            : $"Layout: {(layout.ChannelsFirst ? "NCHW" : "NHWC")} {layout.Size}x{layout.Size}, landmarks from output '{outputName(session, layout.LandmarkOutput)}', face-presence from {(layout.ScoreOutput < 0 ? "(none; assuming a face)" : $"output '{outputName(session, layout.ScoreOutput)}'")}.");
        return string.Join(Environment.NewLine, lines);

        static string outputName(InferenceSession s, int index) => s.OutputNames[index];
    }

    /// <summary>
    /// ONNX Runtime's Windows build can emit trace events to the OS diagnostic-data provider. Gander promises nothing
    /// leaves the PC, so turn that off before any session exists.
    /// </summary>
    private static void EnsureTelemetryOff()
    {
        lock (TelemetryGate)
        {
            if (!_telemetryOff)
            {
                OrtEnv.Instance().DisableTelemetryEvents();
                _telemetryOff = true;
            }
        }
    }

    /// <summary>Opens the model, or returns null and a reason.</summary>
    internal static LandmarkModel? TryOpen(string path, out string message)
    {
        try
        {
            // Read once, hash, then build the session from the very bytes that were hashed (no swap between check and use).
            var bytes = File.ReadAllBytes(path);
            if (!ModelIntegrity.Verify(bytes, out var integrityMessage))
            {
                message = integrityMessage;
                return null;
            }

            EnsureTelemetryOff();

            // A small face-mesh model at 15 fps needs no thread per core and no spinning workers: that only burns CPU
            // and battery on an always-on tray app. One session, opened once; the options are not needed after it exists.
            using var options = new SessionOptions
            {
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                IntraOpNumThreads = IntraOpThreads,
                InterOpNumThreads = 1,
                ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
            };
            options.AddSessionConfigEntry("session.intra_op.allow_spinning", "0");
            options.AddSessionConfigEntry("session.inter_op.allow_spinning", "0");

            var session = new InferenceSession(bytes, options);
            try
            {
                message = Describe(session, bytes.Length, path, out var layout);
                if (layout is null)
                {
                    session.Dispose();
                    return null;
                }

                return new LandmarkModel(session, session.InputNames[0], layout, message);
            }
            catch
            {
                session.Dispose();
                throw;
            }
        }
        catch (Exception exception)
        {
            message = $"Could not load the model: {exception.Message}";
            return null;
        }
    }

    /// <summary>Crops and resizes the box from the frame, runs the model, and maps the landmarks back to frame pixels.</summary>
    internal LandmarkResult? Run(CameraFrame frame, SquareBox box)
    {
        FaceCrop.FillTensor(frame.Pixels, frame.Width, frame.Height, box, _layout.Size, _layout.ChannelsFirst, _input.Buffer.Span);
        using var results = _session.Run(new[] { NamedOnnxValue.CreateFromTensor(_inputName, _input) });

        var landmarks = Output(results, _layout.LandmarkOutput).AsTensor<float>().ToArray();
        if (landmarks.Length < PoseGeometry.MinimumLandmarks * 3)
        {
            return null;
        }

        var presence = 1.0;
        if (_layout.ScoreOutput >= 0)
        {
            var raw = Output(results, _layout.ScoreOutput).AsTensor<float>().GetValue(0);
            presence = raw is >= 0 and <= 1 ? raw : FaceCrop.Sigmoid(raw); // the converted graph outputs a logit
        }

        return new LandmarkResult(FaceCrop.MapToFrame(landmarks, _layout.Size, box), presence);
    }

    // Outputs come back in the session's output order, so the layout's index is the position (no name search per frame).
    private DisposableNamedOnnxValue Output(IReadOnlyCollection<DisposableNamedOnnxValue> results, int index)
    {
        var position = 0;
        foreach (var value in results)
        {
            if (position++ == index)
            {
                return value;
            }
        }

        throw new InvalidOperationException($"The model returned no output '{_outputNames[index]}'.");
    }

    public void Dispose() => _session.Dispose();
}
