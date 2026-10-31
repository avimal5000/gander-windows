using System.Text.Json;

namespace Gander.Core;

/// <summary>
/// One recorded head pose. Numbers and a label only: a trace never holds pixels.
/// </summary>
/// <param name="TimeSeconds">Seconds since the recording started.</param>
/// <param name="Yaw">Degrees, positive toward the user's right (see <see cref="PoseGeometry"/>).</param>
/// <param name="Pitch">Degrees, positive looking up.</param>
/// <param name="Roll">Degrees, positive right ear toward right shoulder.</param>
/// <param name="Label">What the user was asked to look at, such as "monitor 1". Empty when unlabeled.</param>
/// <param name="Round">Which pass of the guided run the sample belongs to (1, 2, ...); 0 when unknown.</param>
/// <param name="Face">False when no face was found in that frame (the angles are then 0 and are ignored).</param>
public sealed record PoseSample(double TimeSeconds, double Yaw, double Pitch, double Roll, string Label, int Round = 0, bool Face = true);

/// <summary>A list of <see cref="PoseSample"/> with JSON reading and writing.</summary>
public sealed class PoseTrace
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    /// <summary>Creates a trace from samples.</summary>
    public PoseTrace(IEnumerable<PoseSample> samples)
    {
        Samples = samples.ToList();
    }

    /// <summary>The samples in recording order.</summary>
    public IReadOnlyList<PoseSample> Samples { get; }

    /// <summary>Writes the trace as JSON.</summary>
    public string ToJson() => JsonSerializer.Serialize(Samples, Options);

    /// <summary>Reads a trace written by <see cref="ToJson"/>. Throws <see cref="JsonException"/> on bad input.</summary>
    public static PoseTrace FromJson(string json) =>
        new(JsonSerializer.Deserialize<List<PoseSample>>(json, Options) ?? throw new JsonException("The trace is empty or null."));
}
