using System.Text.Json;
using System.Text.Json.Serialization;

namespace Gander.Core;

/// <summary>An inclusive range of doubles.</summary>
public readonly record struct DoubleRange(double Lower, double Upper);

/// <summary>
/// The switching settings the user can change (SPEC F21, F35): how long to hold a look, how long to pause after a
/// switch, and how sure Gander must be. The other <see cref="SwitchSettings"/> fields keep their defaults.
/// </summary>
[JsonConverter(typeof(SwitchSensitivityConverter))]
public readonly record struct SwitchSensitivity
{
    /// <summary>Allowed dwell, in seconds.</summary>
    public static readonly DoubleRange DwellRange = new(0.05, 1.5);

    /// <summary>Allowed cooldown, in seconds.</summary>
    public static readonly DoubleRange CooldownRange = new(0.2, 1.5);

    /// <summary>Enter margins from "Relaxed" (index 0) to "Strict". The stay margin is always half the enter margin.</summary>
    public static readonly IReadOnlyList<double> ConfidenceMargins = [0.15, 0.225, 0.30, 0.375, 0.45];

    /// <summary>The defaults: exactly <see cref="SwitchSettings.Default"/>.</summary>
    public static readonly SwitchSensitivity Default = new(0.40, 0.75, 2);

    /// <summary>The "Instant" timing (SPEC D49): a short hold and a short pause, with the default confidence.</summary>
    public static readonly SwitchSensitivity Instant = new(0.10, 0.30, 2);

    /// <summary>Clamps all three values into their allowed ranges.</summary>
    public SwitchSensitivity(double dwell, double cooldown, int confidenceLevel)
    {
        Dwell = Clamp(dwell, DwellRange);
        Cooldown = Clamp(cooldown, CooldownRange);
        ConfidenceLevel = Math.Min(Math.Max(confidenceLevel, 0), ConfidenceMargins.Count - 1);
    }

    /// <summary>Settings saved before the pause was adjustable: the default cooldown (0.75 s).</summary>
    public SwitchSensitivity(double dwell, int confidenceLevel)
        : this(dwell, Default.Cooldown, confidenceLevel)
    {
    }

    /// <summary>Seconds a look must be held before focus switches.</summary>
    public double Dwell { get; init; }

    /// <summary>Seconds after a switch during which no new look starts counting.</summary>
    public double Cooldown { get; init; }

    /// <summary>Index into <see cref="ConfidenceMargins"/>.</summary>
    public int ConfidenceLevel { get; init; }

    /// <summary>These settings with the hold time and pause of <see cref="Instant"/>, and the same confidence.</summary>
    public SwitchSensitivity ApplyingInstantTiming() => new(Instant.Dwell, Instant.Cooldown, ConfidenceLevel);

    /// <summary>The base settings with this dwell, cooldown, and margins.</summary>
    public SwitchSettings ToSwitchSettings(SwitchSettings? baseSettings = null)
    {
        var clamped = new SwitchSensitivity(Dwell, Cooldown, ConfidenceLevel);
        var enter = ConfidenceMargins[clamped.ConfidenceLevel];
        return (baseSettings ?? SwitchSettings.Default) with
        {
            Dwell = clamped.Dwell,
            Cooldown = clamped.Cooldown,
            EnterMargin = enter,
            StayMargin = enter / 2,
        };
    }

    private static double Clamp(double value, DoubleRange range) => Math.Min(Math.Max(value, range.Lower), range.Upper);
}

/// <summary>
/// Reads settings saved by any version: a missing cooldown is the default, out-of-range values are clamped, and a
/// missing dwell or confidence level is an error. Always writes all three values.
/// </summary>
public sealed class SwitchSensitivityConverter : JsonConverter<SwitchSensitivity>
{
    /// <inheritdoc />
    public override SwitchSensitivity Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Expected an object.");
        }

        var dwell = Required(root, "dwell");
        var level = Required(root, "confidenceLevel");
        if (!level.TryGetInt32(out var confidenceLevel))
        {
            throw new JsonException("confidenceLevel must be an integer.");
        }

        var cooldown = SwitchSensitivity.Default.Cooldown;
        if (root.TryGetProperty("cooldown", out var stored) && stored.ValueKind != JsonValueKind.Null)
        {
            cooldown = stored.ValueKind == JsonValueKind.Number ? stored.GetDouble() : throw new JsonException("cooldown must be a number.");
        }

        return new SwitchSensitivity(dwell.GetDouble(), cooldown, confidenceLevel);
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, SwitchSensitivity value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("dwell", value.Dwell);
        writer.WriteNumber("cooldown", value.Cooldown);
        writer.WriteNumber("confidenceLevel", value.ConfidenceLevel);
        writer.WriteEndObject();
    }

    private static JsonElement Required(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value
            : throw new JsonException($"Missing or invalid '{name}'.");
}
