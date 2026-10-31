using System.Runtime.InteropServices;
using Sway.Core.Platform;
using Sway.Platform.Windows.Windowing;

namespace Sway.Platform.Windows.Hotkeys;

/// <summary>
/// Global shortcuts through RegisterHotKey on a dedicated message-pump thread (MOD_NOREPEAT). No keyboard hook, and
/// only the registered combinations are ever delivered. <b>Handlers run on the pump thread</b>: the caller must marshal
/// to its own thread (for example the WPF dispatcher) and keep the handler short. An exception thrown by a handler is
/// swallowed. Registering an action again replaces its previous shortcut.
/// </summary>
public sealed class WindowsHotKeyRegistrar : IHotKeyRegistrar, IDisposable
{
    private const uint WmHotKey = 0x0312;

    private readonly Dictionary<HotKeyAction, Registration> _registrations = [];
    private readonly MessagePumpThread _pump;
    private int _nextId;

    /// <summary>Starts the pump thread.</summary>
    public WindowsHotKeyRegistrar()
    {
        _pump = new MessagePumpThread("Gander hotkeys", OnMessage);
    }

    /// <inheritdoc />
    /// <exception cref="HotKeyException">Invalid, already used by another app, or the system refused.</exception>
    public void Register(HotKeyShortcut shortcut, HotKeyAction action, Action handler)
    {
        ArgumentNullException.ThrowIfNull(shortcut);
        ArgumentNullException.ThrowIfNull(handler);
        if (!shortcut.IsValid || shortcut.VirtualKey == 0)
        {
            throw new HotKeyException(HotKeyErrorKind.InvalidShortcut, $"{shortcut.DisplayString} is not a valid shortcut.");
        }

        _pump.Invoke(() =>
        {
            Remove(action);
            var id = ++_nextId;
            var modifiers = HotKeyMapping.ToNative(shortcut.Modifiers) | HotKeyMapping.ModNoRepeat;
            if (!RegisterHotKey(_pump.Window, id, modifiers, shortcut.VirtualKey))
            {
                throw HotKeyMapping.ErrorFor(shortcut, Marshal.GetLastWin32Error());
            }

            _registrations[action] = new Registration(id, handler);
            return true;
        });
    }

    /// <inheritdoc />
    public void Unregister(HotKeyAction action)
    {
        _pump.Invoke(() =>
        {
            Remove(action);
            return true;
        });
    }

    /// <summary>Unregisters everything and stops the pump thread.</summary>
    public void Dispose()
    {
        try
        {
            _pump.Invoke(() =>
            {
                foreach (var action in _registrations.Keys.ToList())
                {
                    Remove(action);
                }

                return true;
            });
        }
        catch (ObjectDisposedException)
        {
            // Already disposed.
        }

        _pump.Dispose();
    }

    private void Remove(HotKeyAction action)
    {
        if (_registrations.Remove(action, out var existing))
        {
            UnregisterHotKey(_pump.Window, existing.Id);
        }
    }

    private bool OnMessage(uint message, nint wParam, nint lParam)
    {
        if (message != WmHotKey)
        {
            return false;
        }

        var id = (int)wParam;
        foreach (var registration in _registrations.Values)
        {
            if (registration.Id == id)
            {
                registration.Handler();
                break;
            }
        }

        return true;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(nint window, int id);

    private sealed record Registration(int Id, Action Handler);
}
