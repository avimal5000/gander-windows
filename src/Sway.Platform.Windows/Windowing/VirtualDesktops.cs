using System.Runtime.InteropServices;

namespace Sway.Platform.Windows.Windowing;

/// <summary>Asks Windows whether a window is on the virtual desktop the user is looking at (IVirtualDesktopManager).</summary>
internal static class VirtualDesktops
{
    // One manager per thread: the watcher's thread and the focus worker each create their own COM object.
    private static readonly ThreadLocal<IVirtualDesktopManager?> Manager = new(() =>
    {
        try
        {
            return (IVirtualDesktopManager)new VirtualDesktopManagerClass();
        }
        catch (Exception exception) when (exception is COMException or InvalidCastException)
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

        try
        {
            return manager.IsWindowOnCurrentVirtualDesktop(window, out var onCurrent) != 0 || onCurrent;
        }
        catch (COMException)
        {
            return true;
        }
    }

    /// <summary>Handle and on-current-desktop flag of each visible, unowned top-level window. Handles and flags only.</summary>
    internal static List<KeyValuePair<long, bool>> VisibleWindowFlags()
    {
        var found = new List<KeyValuePair<long, bool>>();
        EnumWindows(
            (window, _) =>
            {
                if (IsWindowVisible(window) && GetWindow(window, GwOwner) == IntPtr.Zero)
                {
                    found.Add(KeyValuePair.Create(window.ToInt64(), IsOnCurrent(window)));
                }

                return true;
            },
            IntPtr.Zero);
        return found;
    }

    private const int GwOwner = 4;

    private delegate bool WindowEnumProc(IntPtr window, IntPtr data);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(WindowEnumProc proc, IntPtr data);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr window, int command);

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
}
