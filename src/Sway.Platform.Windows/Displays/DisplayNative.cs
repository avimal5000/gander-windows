using System.Runtime.InteropServices;
using Sway.Core;

namespace Sway.Platform.Windows.Displays;

/// <summary>
/// The real <see cref="IDisplayNative"/>: EnumDisplayMonitors for rectangles, QueryDisplayConfig plus
/// DisplayConfigGetDeviceInfo for the monitor's device path, friendly name and connection type. Rectangles are physical
/// pixels only when the process is per-monitor DPI aware (the app manifest does that).
/// </summary>
internal sealed class DisplayNative : IDisplayNative
{
    private const uint QdcOnlyActivePaths = 2;
    private const int ErrorInsufficientBuffer = 122;
    private const int GetSourceName = 1;
    private const int GetTargetName = 2;

    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, ref Rect rect, IntPtr data);

    public IReadOnlyList<MonitorSnapshot> Monitors()
    {
        var found = new List<MonitorSnapshot>();
        EnumDisplayMonitors(
            IntPtr.Zero,
            IntPtr.Zero,
            (IntPtr monitor, IntPtr hdc, ref Rect rect, IntPtr data) =>
            {
                var info = new MonitorInfoEx { Size = Marshal.SizeOf<MonitorInfoEx>() };
                if (GetMonitorInfoW(monitor, ref info))
                {
                    var bounds = new DisplayRect(info.Monitor.Left, info.Monitor.Top, info.Monitor.Right - info.Monitor.Left, info.Monitor.Bottom - info.Monitor.Top);
                    found.Add(new MonitorSnapshot(monitor.ToInt64(), info.Device, bounds, (info.Flags & 1) != 0));
                }

                return true;
            },
            IntPtr.Zero);
        return found;
    }

    public IReadOnlyDictionary<string, DisplayTarget> Targets()
    {
        var result = new Dictionary<string, DisplayTarget>(StringComparer.OrdinalIgnoreCase);
        PathInfo[] paths = [];
        var attempts = 0;
        int status;
        do
        {
            if (GetDisplayConfigBufferSizes(QdcOnlyActivePaths, out var pathCount, out var modeCount) != 0)
            {
                return result;
            }

            paths = new PathInfo[pathCount];
            var modes = new ModeInfo[modeCount];
            status = QueryDisplayConfig(QdcOnlyActivePaths, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
            if (status == 0)
            {
                Array.Resize(ref paths, (int)pathCount);
            }
        }
        while (status == ErrorInsufficientBuffer && ++attempts < 4);

        if (status != 0)
        {
            return result;
        }

        foreach (var path in paths)
        {
            var source = new SourceDeviceName { Header = Header(GetSourceName, path.SourceAdapter, path.SourceId, Marshal.SizeOf<SourceDeviceName>()) };
            var target = new TargetDeviceName { Header = Header(GetTargetName, path.TargetAdapter, path.TargetId, Marshal.SizeOf<TargetDeviceName>()) };
            if (DisplayConfigGetDeviceInfo(ref source) == 0 && DisplayConfigGetDeviceInfo(ref target) == 0)
            {
                result[source.GdiName] = new DisplayTarget(target.DevicePath, target.FriendlyName, path.TargetOutputTechnology);
            }
        }

        return result;
    }

    private static DeviceInfoHeader Header(int type, Luid adapter, uint id, int size) =>
        new() { Type = type, Size = (uint)size, Adapter = adapter, Id = id };

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string Device;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid
    {
        public uint LowPart;
        public int HighPart;
    }

    // DISPLAYCONFIG_PATH_INFO (72 bytes): source info (20), target info (48), flags (4).
    [StructLayout(LayoutKind.Sequential)]
    private struct PathInfo
    {
        public Luid SourceAdapter;
        public uint SourceId;
        public uint SourceModeIndex;
        public uint SourceStatusFlags;
        public Luid TargetAdapter;
        public uint TargetId;
        public uint TargetModeIndex;
        public uint TargetOutputTechnology;
        public uint TargetRotation;
        public uint TargetScaling;
        public uint RefreshNumerator;
        public uint RefreshDenominator;
        public uint TargetScanLineOrdering;
        public int TargetAvailable;
        public uint TargetStatusFlags;
        public uint Flags;
    }

    // DISPLAYCONFIG_MODE_INFO is 64 bytes; only its size matters here.
    [StructLayout(LayoutKind.Sequential, Size = 64)]
    private struct ModeInfo
    {
        public uint InfoType;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceInfoHeader
    {
        public int Type;
        public uint Size;
        public Luid Adapter;
        public uint Id;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SourceDeviceName
    {
        public DeviceInfoHeader Header;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string GdiName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct TargetDeviceName
    {
        public DeviceInfoHeader Header;
        public uint Flags;
        public uint OutputTechnology;
        public ushort EdidManufactureId;
        public ushort EdidProductCodeId;
        public uint ConnectorInstance;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string FriendlyName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DevicePath;
    }

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfoW(IntPtr monitor, ref MonitorInfoEx info);

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(uint flags, out uint pathCount, out uint modeCount);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(uint flags, ref uint pathCount, [Out] PathInfo[] paths, ref uint modeCount, [Out] ModeInfo[] modes, IntPtr topology);

    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")]
    private static extern int DisplayConfigGetDeviceInfo(ref SourceDeviceName packet);

    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")]
    private static extern int DisplayConfigGetDeviceInfo(ref TargetDeviceName packet);
}
