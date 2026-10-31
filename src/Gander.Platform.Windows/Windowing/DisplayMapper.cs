using Gander.Core;

namespace Gander.Platform.Windows.Windowing;

/// <summary>Maps a monitor handle to the <see cref="DisplayId"/> of the layout. Pure.</summary>
public static class DisplayMapper
{
    /// <summary>
    /// A lookup for the given layout. A monitor matches the display whose <c>NativeId</c> equals the handle's low 32
    /// bits (what the Windows display catalog stores); failing that, the display whose frame equals the monitor's
    /// rectangle (covers a layout that was built by something else). Unknown monitors map to null.
    /// </summary>
    public static Func<long, DisplayId?> Create(DisplayLayout layout, Func<long, DisplayRect?> boundsOfMonitor)
    {
        var cache = new Dictionary<long, DisplayId?>();
        return handle =>
        {
            if (cache.TryGetValue(handle, out var known))
            {
                return known;
            }

            DisplayId? found = null;
            var native = unchecked((uint)handle);
            foreach (var display in layout.Displays)
            {
                if (display.NativeId == native)
                {
                    found = display.Id;
                    break;
                }
            }

            if (found is null && boundsOfMonitor(handle) is { } bounds)
            {
                foreach (var display in layout.Displays)
                {
                    if (display.Frame == bounds)
                    {
                        found = display.Id;
                        break;
                    }
                }
            }

            cache[handle] = found;
            return found;
        };
    }
}
