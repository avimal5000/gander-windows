using System.Text.Json;
using System.Text.Json.Serialization;

namespace Gander.Core;

/// <summary>JSON options shared by the settings and calibration types (camelCase, like the Mac app's files).</summary>
public static class EngineJson
{
    /// <summary>The options.</summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };
}

/// <summary>One display's calibrated head pose: where the head points while looking at that display.</summary>
/// <param name="DisplayId">The display.</param>
/// <param name="Yaw">Mean of the display's face samples, degrees, in the profile's feature set.</param>
/// <param name="Pitch">Mean pitch.</param>
/// <param name="YawStd">Sample standard deviation (n - 1), degrees, unfloored. Informational.</param>
/// <param name="PitchStd">Pitch standard deviation.</param>
/// <param name="SampleCount">Face samples the means came from.</param>
public sealed record DisplayProfile(
    [property: JsonRequired] DisplayId DisplayId,
    [property: JsonRequired] double Yaw,
    [property: JsonRequired] double Pitch,
    [property: JsonRequired] double YawStd,
    [property: JsonRequired] double PitchStd,
    [property: JsonRequired] int SampleCount);

/// <summary>
/// Per-display pose profiles fitted from a labeled trace. Plain data, so it can be stored and shown anywhere;
/// <see cref="PoseSwitchEngine"/> fits it and classifies against it.
/// </summary>
/// <param name="Features">Which pose features the profile uses. Live tracking uses VisionAngles.</param>
/// <param name="Displays">Profiles of the displays that had enough face samples, in the trace layout's left-to-right order.</param>
/// <param name="YawScale">Pooled within-display yaw standard deviation, floored. Normalizes distances.</param>
/// <param name="PitchScale">Pooled pitch standard deviation, floored; never less than YawScale.</param>
/// <param name="LayoutFingerprint">Fingerprint of the layout the trace was recorded on. A different current fingerprint means the profile is stale.</param>
/// <param name="CameraName">Camera the trace was recorded with; poses from another camera do not match the profile.</param>
/// <param name="CreatedAt">When the trace was recorded.</param>
/// <param name="FormatVersion">Format version; a profile with another version is refitted, not reused.</param>
public sealed record CalibrationProfile(
    [property: JsonRequired] PoseFeatureSet Features,
    [property: JsonRequired] IReadOnlyList<DisplayProfile> Displays,
    [property: JsonRequired] double YawScale,
    [property: JsonRequired] double PitchScale,
    [property: JsonRequired] string LayoutFingerprint,
    string? CameraName,
    [property: JsonRequired] DateTimeOffset CreatedAt,
    [property: JsonRequired] int FormatVersion = CalibrationProfile.CurrentFormatVersion)
{
    /// <summary>Bump when stored fields change meaning; a profile with another version is refitted, not reused.</summary>
    public const int CurrentFormatVersion = 1;

    /// <summary>The library key: this profile's display set and camera.</summary>
    [JsonIgnore]
    public CalibrationKey Key => new(Displays.Select(d => d.DisplayId), CameraName);

    /// <summary>The displays this profile covers.</summary>
    [JsonIgnore]
    public IReadOnlySet<DisplayId> DisplayIds => Displays.Select(d => d.DisplayId).ToHashSet();

    /// <summary>The profile of one display, or null.</summary>
    public DisplayProfile? ProfileFor(DisplayId id) => Displays.FirstOrDefault(d => d.DisplayId == id);

    /// <summary>True when the profile was fitted on this exact display arrangement and is in the current format.</summary>
    public bool Matches(DisplayLayout layout) =>
        FormatVersion == CurrentFormatVersion && LayoutFingerprint == layout.Fingerprint;

    /// <summary>
    /// The same profile with only the displays in <paramref name="ids"/>, in the saved order. Scales and metadata are
    /// kept, so poses classify exactly as before among the remaining displays.
    /// </summary>
    public CalibrationProfile RestrictedTo(IReadOnlySet<DisplayId> ids) =>
        new(Features, Displays.Where(d => ids.Contains(d.DisplayId)).ToList(), YawScale, PitchScale, LayoutFingerprint, CameraName, CreatedAt, FormatVersion);

    /// <inheritdoc />
    public bool Equals(CalibrationProfile? other) =>
        other is not null && FormatVersion == other.FormatVersion && Features == other.Features
        && Displays.SequenceEqual(other.Displays) && YawScale.Equals(other.YawScale) && PitchScale.Equals(other.PitchScale)
        && LayoutFingerprint == other.LayoutFingerprint && CameraName == other.CameraName && CreatedAt == other.CreatedAt;

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(FormatVersion, Features, Displays.Count, YawScale, PitchScale, LayoutFingerprint, CameraName, CreatedAt);
}

/// <summary>Identifies a saved calibration: the displays it profiles and the camera it was recorded with.</summary>
public sealed class CalibrationKey : IEquatable<CalibrationKey>
{
    /// <summary>Creates a key; the ids are sorted by raw value.</summary>
    public CalibrationKey(IEnumerable<DisplayId> displayIds, string? cameraName)
    {
        DisplayIds = displayIds.OrderBy(id => id.RawValue, StringComparer.Ordinal).ToList();
        CameraName = cameraName;
    }

    /// <summary>The profiled displays, sorted by raw value.</summary>
    public IReadOnlyList<DisplayId> DisplayIds { get; }

    /// <summary>The camera, or null.</summary>
    public string? CameraName { get; }

    /// <inheritdoc />
    public bool Equals(CalibrationKey? other) =>
        other is not null && CameraName == other.CameraName && DisplayIds.SequenceEqual(other.DisplayIds);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as CalibrationKey);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(DisplayIds.Count, CameraName);
}

/// <summary>The calibration chosen for the current displays and camera.</summary>
/// <param name="Profile">What to classify against: the saved profile restricted to the connected displays.</param>
/// <param name="Saved">The profile as saved; its key identifies it in the library.</param>
public sealed record CalibrationMatch(CalibrationProfile Profile, CalibrationProfile Saved)
{
    /// <summary>True when the saved profile covers displays that are not connected now.</summary>
    public bool IsPartial => Saved.Displays.Count > Profile.Displays.Count;
}

/// <summary>
/// Every saved calibration, at most one per display set and camera (SPEC D24). A calibration belongs to the set of
/// displays it profiles, not to their exact arrangement, and also covers any two or more of its displays.
/// </summary>
[JsonConverter(typeof(CalibrationLibraryConverter))]
public sealed class CalibrationLibrary : IEquatable<CalibrationLibrary>
{
    /// <summary>Bump when the stored shape changes meaning.</summary>
    public const int CurrentFormatVersion = 1;

    /// <summary>Most profiles kept; saving another drops the least recently saved.</summary>
    public const int MaxProfiles = 24;

    private readonly List<CalibrationProfile> _profiles = [];

    /// <summary>Creates a library from profiles given newest first.</summary>
    public CalibrationLibrary(IEnumerable<CalibrationProfile>? profiles = null)
    {
        foreach (var profile in (profiles ?? []).Reverse())
        {
            Save(profile);
        }
    }

    /// <summary>Newest first.</summary>
    public IReadOnlyList<CalibrationProfile> Profiles => _profiles;

    /// <summary>
    /// Saves the profile as the newest, replacing a saved profile with the same key. Profiles with fewer than two
    /// displays or another format version are ignored: they can never be used.
    /// </summary>
    public void Save(CalibrationProfile profile)
    {
        if (profile.Displays.Count < 2 || profile.FormatVersion != CalibrationProfile.CurrentFormatVersion)
        {
            return;
        }

        var key = profile.Key;
        _profiles.RemoveAll(p => p.Key.Equals(key));
        _profiles.Insert(0, profile);
        if (_profiles.Count > MaxProfiles)
        {
            _profiles.RemoveRange(MaxProfiles, _profiles.Count - MaxProfiles);
        }
    }

    /// <summary>Removes the profile saved under the key, if any.</summary>
    public void Remove(CalibrationKey key) => _profiles.RemoveAll(p => p.Key.Equals(key));

    /// <summary>
    /// The calibration to track with on the layout using the camera named <paramref name="cameraName"/>; null if none
    /// fits. Candidates are profiles for the same camera whose displays include every display in the layout (at least
    /// two). The candidate with the fewest displays wins, so an exact set comes first; ties go to the most recently saved.
    /// </summary>
    public CalibrationMatch? Resolve(DisplayLayout layout, string? cameraName)
    {
        var connected = layout.Displays.Select(d => d.Id).ToHashSet();
        if (connected.Count < 2)
        {
            return null;
        }

        var best = _profiles
            .Select((profile, offset) => (profile, offset))
            .Where(x => x.profile.CameraName == cameraName && connected.IsSubsetOf(x.profile.DisplayIds))
            .OrderBy(x => x.profile.Displays.Count)
            .ThenBy(x => x.offset)
            .Select(x => x.profile)
            .FirstOrDefault();
        return best is null ? null : new CalibrationMatch(best.RestrictedTo(connected), best);
    }

    /// <inheritdoc />
    public bool Equals(CalibrationLibrary? other) => other is not null && _profiles.SequenceEqual(other._profiles);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as CalibrationLibrary);

    /// <inheritdoc />
    public override int GetHashCode() => _profiles.Count;
}

/// <summary>
/// Reads and writes the library. Decodes leniently: a profile that fails to decode, or has another format version, is
/// dropped instead of failing the whole library. A library of another format version decodes as empty.
/// </summary>
public sealed class CalibrationLibraryConverter : JsonConverter<CalibrationLibrary>
{
    /// <inheritdoc />
    public override CalibrationLibrary Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("formatVersion", out var version) || version.ValueKind != JsonValueKind.Number)
        {
            throw new JsonException("The library has no formatVersion.");
        }

        if (version.GetInt32() != CalibrationLibrary.CurrentFormatVersion)
        {
            return new CalibrationLibrary();
        }

        if (!root.TryGetProperty("profiles", out var profiles) || profiles.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("The library has no profiles list.");
        }

        var stored = new List<CalibrationProfile>();
        foreach (var element in profiles.EnumerateArray())
        {
            try
            {
                if (element.Deserialize<CalibrationProfile>(EngineJson.Options) is { } profile && IsWellFormed(profile))
                {
                    stored.Add(profile);
                }
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // A broken profile (bad JSON, or null where a value is required) is dropped, not fatal.
            }
        }

        return new CalibrationLibrary(stored);
    }

    /// <summary>
    /// System.Text.Json accepts JSON null for non-nullable reference properties, so a decoded profile can still hold
    /// nulls or nonsense numbers. Anything unusable is dropped.
    /// </summary>
    private static bool IsWellFormed(CalibrationProfile profile) =>
        profile.Displays is not null
        && profile.LayoutFingerprint is not null
        && profile.Displays.All(d => d is not null && d.DisplayId.RawValue is not null && double.IsFinite(d.Yaw)
            && double.IsFinite(d.Pitch) && double.IsFinite(d.YawStd) && double.IsFinite(d.PitchStd) && d.SampleCount >= 0)
        && double.IsFinite(profile.YawScale) && profile.YawScale > 0
        && double.IsFinite(profile.PitchScale) && profile.PitchScale > 0;

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, CalibrationLibrary value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("formatVersion", CalibrationLibrary.CurrentFormatVersion);
        writer.WritePropertyName("profiles");
        JsonSerializer.Serialize(writer, value.Profiles, EngineJson.Options);
        writer.WriteEndObject();
    }
}
