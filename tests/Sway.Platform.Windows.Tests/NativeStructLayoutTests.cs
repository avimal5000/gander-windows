using System.Reflection;
using System.Runtime.InteropServices;
using Sway.Platform.Windows.Displays;
using Sway.Platform.Windows.Windowing;

namespace Sway.Platform.Windows.Tests;

/// <summary>
/// The P/Invoke structs must match the Win32 layouts exactly (audit F34): a slip of one field shifts everything after it
/// and the API returns garbage or fails. Sizes are the documented x64 sizes; the tests run on the Windows CI runner.
/// </summary>
public class NativeStructLayoutTests
{
    private static int SizeOf(Type owner, string nested)
    {
        var type = owner.GetNestedType(nested, BindingFlags.NonPublic | BindingFlags.Public)
            ?? throw new InvalidOperationException($"{owner.Name}.{nested} not found");
        return Marshal.SizeOf(type);
    }

    [Theory]
    [InlineData("PathInfo", 72)] // DISPLAYCONFIG_PATH_INFO
    [InlineData("ModeInfo", 64)] // DISPLAYCONFIG_MODE_INFO
    [InlineData("MonitorInfoEx", 104)] // MONITORINFOEXW
    [InlineData("SourceDeviceName", 84)] // DISPLAYCONFIG_SOURCE_DEVICE_NAME
    [InlineData("TargetDeviceName", 420)] // DISPLAYCONFIG_TARGET_DEVICE_NAME
    [InlineData("Rect", 16)]
    public void DisplayNativeStructsMatchWin32(string name, int expected)
    {
        Assert.Equal(expected, SizeOf(typeof(DisplayNative), name));
    }

    [Fact]
    public void WindowNativeCallsStructsMatchWin32()
    {
        Assert.Equal(40, SizeOf(typeof(WindowNativeCalls), "MonitorInfo")); // MONITORINFO
        Assert.Equal(16, SizeOf(typeof(WindowNativeCalls), "Rect"));
    }

    [Fact]
    public void MessagePumpStructsMatchWin32OnX64()
    {
        if (IntPtr.Size != 8)
        {
            return;
        }

        Assert.Equal(80, SizeOf(typeof(MessagePumpThread), "WndClassEx")); // WNDCLASSEXW
        Assert.Equal(48, SizeOf(typeof(MessagePumpThread), "Msg")); // MSG
    }

    [Fact]
    public void SessionStructsMatchWin32OnX64()
    {
        if (IntPtr.Size != 8)
        {
            return;
        }

        Assert.Equal(80, Marshal.SizeOf<SessionNative.WindowClass>());
        Assert.Equal(48, Marshal.SizeOf<SessionNative.Message>());
    }
}
