using Gander.Core;
using Gander.Core.Platform;
using Gander.Platform.Windows.Windowing;

namespace Gander.Platform.Windows.Displays;

/// <summary>
/// The monitors as the engine sees them. <see cref="Current"/> reads the live configuration each call (cheap, and
/// always fresh). <see cref="LayoutChanged"/> is raised from a dedicated pump thread when Windows sends WM_DISPLAYCHANGE
/// or WM_DPICHANGED, possibly several times for one real change; the handler must marshal and must not assume the
/// layout differs. Display ids come from the monitor's device path (see <see cref="DisplayLayoutBuilder.StableId"/>).
/// </summary>
public sealed class WindowsDisplayCatalog : IDisplayCatalog, IDisposable
{
    private const uint WmDisplayChange = 0x007E;
    private const uint WmDpiChanged = 0x02E0;

    private readonly IDisplayNative _native;
    private readonly MessagePumpThread? _pump;

    /// <summary>Starts listening for display changes.</summary>
    public WindowsDisplayCatalog()
        : this(new DisplayNative(), listen: true)
    {
    }

    internal WindowsDisplayCatalog(IDisplayNative native, bool listen)
    {
        _native = native;
        if (listen)
        {
            _pump = new MessagePumpThread("Gander display changes", OnMessage);
        }
    }

    /// <inheritdoc />
    public event Action? LayoutChanged;

    /// <inheritdoc />
    public DisplayLayout Current()
    {
        try
        {
            var monitors = _native.Monitors();
            var targets = monitors.Count == 0 ? new Dictionary<string, DisplayTarget>() : _native.Targets();
            return DisplayLayoutBuilder.Build(monitors, targets);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return DisplayLayout.Empty;
        }
    }

    /// <summary>Raises <see cref="LayoutChanged"/> as a real notification would (tests and manual refresh).</summary>
    internal void NotifyChanged() => LayoutChanged?.Invoke();

    /// <summary>Stops listening.</summary>
    public void Dispose() => _pump?.Dispose();

    private bool OnMessage(uint message, nint wParam, nint lParam)
    {
        if (message is WmDisplayChange or WmDpiChanged)
        {
            LayoutChanged?.Invoke();
            return true;
        }

        return false;
    }
}
