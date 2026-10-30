using System.Text.RegularExpressions;
using Sway.Core;

namespace Sway.Platform.Windows.Displays;

/// <summary>Builds the engine's <see cref="DisplayLayout"/> from raw monitor and target data. Pure.</summary>
public static partial class DisplayLayoutBuilder
{
    // DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY_INTERNAL (0x80000000), _DISPLAYPORT_EMBEDDED (11), _UDI_EMBEDDED (13).
    private const uint OutputInternal = 0x80000000;
    private const uint OutputDisplayPortEmbedded = 11;
    private const uint OutputUdiEmbedded = 13;

    /// <summary>True for a laptop's own panel.</summary>
    public static bool IsBuiltIn(uint outputTechnology) =>
        outputTechnology is OutputInternal or OutputDisplayPortEmbedded or OutputUdiEmbedded;

    /// <summary>
    /// The stable id for a monitor: its device path lower-cased, without the trailing interface GUID (the part that
    /// names the device class, not the monitor). The path carries the EDID manufacturer and product code and the
    /// instance identity, so it is the same across runs for the same monitor on the same connection. Without a path the
    /// id falls back to <c>gdi:</c> plus the GDI device name.
    /// </summary>
    public static string StableId(string? devicePath, string gdiName)
    {
        if (string.IsNullOrWhiteSpace(devicePath))
        {
            return "gdi:" + gdiName.Trim().ToLowerInvariant();
        }

        var trimmed = InterfaceGuid().Replace(devicePath.Trim(), string.Empty);
        return trimmed.ToLowerInvariant();
    }

    /// <summary>
    /// One <see cref="DisplayInfo"/> per monitor. Two monitors that would get the same id (identical models with an
    /// unreadable instance id, or the fallback) are told apart with a <c>#2</c>, <c>#3</c> suffix in left-to-right order, so
    /// ids are always unique.
    /// </summary>
    public static DisplayLayout Build(IEnumerable<MonitorSnapshot> monitors, IReadOnlyDictionary<string, DisplayTarget> targets)
    {
        var used = new Dictionary<string, int>(StringComparer.Ordinal);
        var infos = new List<DisplayInfo>();
        var ordered = monitors
            .OrderBy(m => m.Bounds.MinX)
            .ThenBy(m => m.Bounds.MinY)
            .ThenBy(m => m.DeviceName, StringComparer.Ordinal);
        foreach (var monitor in ordered)
        {
            targets.TryGetValue(monitor.DeviceName, out var target);
            if (target is null)
            {
                target = targets.FirstOrDefault(pair => string.Equals(pair.Key, monitor.DeviceName, StringComparison.OrdinalIgnoreCase)).Value;
            }

            var id = StableId(target?.DevicePath, monitor.DeviceName);
            var count = used.GetValueOrDefault(id) + 1;
            used[id] = count;
            if (count > 1)
            {
                id = $"{id}#{count}";
            }

            var builtIn = target is not null && IsBuiltIn(target.OutputTechnology);
            var name = !string.IsNullOrWhiteSpace(target?.FriendlyName)
                ? target!.FriendlyName.Trim()
                : builtIn ? "Built-in display" : "Display";
            infos.Add(new DisplayInfo(new DisplayId(id), unchecked((uint)monitor.Handle), name, monitor.Bounds, builtIn, monitor.IsPrimary));
        }

        return new DisplayLayout(infos);
    }

    [GeneratedRegex(@"#\{[0-9a-fA-F\-]{36}\}$")]
    private static partial Regex InterfaceGuid();
}
