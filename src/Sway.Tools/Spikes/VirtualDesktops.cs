using System.Runtime.InteropServices;

namespace Sway.Tools.Spikes;

/// <summary>Asks Windows whether a window is on the virtual desktop the user is looking at.</summary>
internal static class VirtualDesktops
{
    [ComImport]
    [Guid("a5cd92ff-29be-454c-8d04-d82879fb3f1b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IVirtualDesktopManager
    {
        [PreserveSig]
        int IsWindowOnCurrentVirtualDesktop(IntPtr window, [MarshalAs(UnmanagedType.Bool)] out bool onCurrent);
    }

    [ComImport]
    [Guid("aa509086-5ca9-4c25-8f95-589d3c07b48a")]
    private class VirtualDesktopManagerClass
    {
    }

    private static readonly Lazy<IVirtualDesktopManager?> Manager = new(() =>
    {
        try
        {
            return (IVirtualDesktopManager)new VirtualDesktopManagerClass();
        }
        catch (COMException)
        {
            return null;
        }
    });

    /// <summary>True when the window is on the current desktop, or when Windows cannot say (then it is assumed to be).</summary>
    internal static bool IsOnCurrent(IntPtr window)
    {
        var manager = Manager.Value;
        if (manager is null)
        {
            return true;
        }

        return manager.IsWindowOnCurrentVirtualDesktop(window, out var onCurrent) != 0 || onCurrent;
    }
}
