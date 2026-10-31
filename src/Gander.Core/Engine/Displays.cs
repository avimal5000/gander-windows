namespace Gander.Core;

/// <summary>
/// Stable display identity. On Windows this is the monitor's device path or EDID-based id; it survives reboots and
/// reconnects for the same physical monitor. Mirrors the Mac <c>DisplayID</c>; serialized as <c>{"rawValue": ...}</c>.
/// </summary>
public readonly record struct DisplayId(string RawValue)
{
    /// <summary>Lets tests and callers write a string where a display id is expected.</summary>
    public static implicit operator DisplayId(string rawValue) => new(rawValue);

    /// <inheritdoc />
    public override string ToString() => RawValue;
}

/// <summary>A rectangle in global desktop coordinates (pixels).</summary>
public readonly record struct DisplayRect(double X, double Y, double Width, double Height)
{
    /// <summary>Left edge.</summary>
    public double MinX => X;

    /// <summary>Top edge.</summary>
    public double MinY => Y;

    /// <summary>
    /// The smallest rectangle with whole-number edges that contains this one (origin rounded down, far edges rounded
    /// up), like the Mac <c>CGRect.integral</c>.
    /// </summary>
    public DisplayRect Integral()
    {
        var minX = Math.Floor(X);
        var minY = Math.Floor(Y);
        var maxX = Math.Ceiling(X + Width);
        var maxY = Math.Ceiling(Y + Height);
        return new DisplayRect(minX, minY, maxX - minX, maxY - minY);
    }
}

/// <summary>One connected display.</summary>
/// <param name="Id">Stable identity.</param>
/// <param name="NativeId">The operating system's handle for the current session only; never persist as identity.</param>
/// <param name="Name">Friendly name.</param>
/// <param name="Frame">Global desktop rectangle.</param>
/// <param name="IsBuiltIn">True for a laptop's own panel.</param>
/// <param name="IsMain">True for the primary display.</param>
public sealed record DisplayInfo(DisplayId Id, uint NativeId, string Name, DisplayRect Frame, bool IsBuiltIn, bool IsMain);

/// <summary>The current arrangement of displays.</summary>
public sealed class DisplayLayout : IEquatable<DisplayLayout>
{
    /// <summary>The layout with no displays.</summary>
    public static readonly DisplayLayout Empty = new([]);

    /// <summary>Creates a layout. Displays are sorted left to right by frame X, then Y, then id.</summary>
    public DisplayLayout(IEnumerable<DisplayInfo> displays)
    {
        Displays = displays
            .OrderBy(d => d.Frame.MinX)
            .ThenBy(d => d.Frame.MinY)
            .ThenBy(d => d.Id.RawValue, StringComparer.Ordinal)
            .ToList();
        Fingerprint = string.Join(
            "|",
            Displays.Select(d =>
            {
                var frame = d.Frame.Integral();
                return FormattableString.Invariant(
                    $"{d.Id.RawValue}@{(long)frame.MinX},{(long)frame.MinY},{(long)frame.Width}x{(long)frame.Height}");
            }));
    }

    /// <summary>Sorted left to right by frame X, then Y, then id.</summary>
    public IReadOnlyList<DisplayInfo> Displays { get; }

    /// <summary>
    /// Canonical identity of this arrangement, built from display ids and frames (not native ids). The same physical
    /// arrangement always yields the same fingerprint.
    /// </summary>
    public string Fingerprint { get; }

    /// <summary>The display with this id, or null.</summary>
    public DisplayInfo? Display(DisplayId id) => Displays.FirstOrDefault(d => d.Id == id);

    /// <inheritdoc />
    public bool Equals(DisplayLayout? other) =>
        other is not null && Displays.SequenceEqual(other.Displays);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as DisplayLayout);

    /// <inheritdoc />
    public override int GetHashCode() => Fingerprint.GetHashCode(StringComparison.Ordinal);
}
