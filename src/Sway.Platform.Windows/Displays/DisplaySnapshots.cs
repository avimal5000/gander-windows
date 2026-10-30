using Sway.Core;

namespace Sway.Platform.Windows.Displays;

/// <summary>One monitor as EnumDisplayMonitors reports it.</summary>
/// <param name="Handle">HMONITOR (valid for the current display configuration only).</param>
/// <param name="DeviceName">GDI name such as <c>\\.\DISPLAY1</c> (can change between runs; used only to join with the target info).</param>
/// <param name="Bounds">Physical pixels, virtual-screen coordinates.</param>
/// <param name="IsPrimary">MONITORINFOF_PRIMARY.</param>
public sealed record MonitorSnapshot(long Handle, string DeviceName, DisplayRect Bounds, bool IsPrimary);

/// <summary>The physical monitor behind a GDI display, from DISPLAYCONFIG_TARGET_DEVICE_NAME.</summary>
/// <param name="DevicePath">monitorDevicePath, e.g. <c>\\?\DISPLAY#GSM5B09#5&amp;2a5e3c3f&amp;0&amp;UID4352#{guid}</c>.</param>
/// <param name="FriendlyName">monitorFriendlyDeviceName (empty for some internal panels).</param>
/// <param name="OutputTechnology">DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY value.</param>
public sealed record DisplayTarget(string DevicePath, string FriendlyName, uint OutputTechnology);

/// <summary>Reads the current monitors; the real one calls Win32, tests use a fake.</summary>
internal interface IDisplayNative
{
    IReadOnlyList<MonitorSnapshot> Monitors();

    /// <summary>Target info keyed by the GDI device name of the source (case-insensitive). May be empty if QueryDisplayConfig fails.</summary>
    IReadOnlyDictionary<string, DisplayTarget> Targets();
}
