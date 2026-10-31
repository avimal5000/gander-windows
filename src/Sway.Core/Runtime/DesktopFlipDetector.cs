namespace Sway.Core.Runtime;

/// <summary>
/// Notices a virtual desktop switch from the outside, with no special API: when the user changes desktop, windows of
/// the desktop left behind stop being "on the current desktop" and the new desktop's windows start being so. Feed it
/// the window handles and their on-current-desktop flags each time; it answers true when a window it saw before has
/// flipped. Windows that open, close or are pinned to every desktop never flip. Pure; not thread safe.
/// </summary>
public sealed class DesktopFlipDetector
{
    private Dictionary<long, bool> _last = [];

    /// <summary>Records the current flags. True when at least one window seen last time now has the other flag.</summary>
    public bool Update(IEnumerable<KeyValuePair<long, bool>> windows)
    {
        var next = new Dictionary<long, bool>();
        var flipped = false;
        foreach (var (handle, onCurrent) in windows)
        {
            next[handle] = onCurrent;
            if (_last.TryGetValue(handle, out var before) && before != onCurrent)
            {
                flipped = true;
            }
        }

        _last = next;
        return flipped;
    }

    /// <summary>Forgets what was seen, so the next update only records.</summary>
    public void Reset() => _last = [];
}
