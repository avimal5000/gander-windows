using System.Runtime.InteropServices;
using Sway.Core.Platform;

namespace Sway.Platform.Windows;

/// <summary>
/// Lock, unlock, console connect and disconnect, sleep, wake and display sleep, from a message-only window on its own
/// thread (the window handles session notifications, suspend and resume notifications and the console-display-state
/// power setting). Events are raised on that thread, outside any lock, so a handler may call back in; it should hand
/// the work to its own thread quickly. Windows may send some events more than once (a user wake sends two resume
/// codes); the suspend tracker ignores repeats. Dispose unregisters everything and ends the thread.
/// </summary>
/// <remarks>
/// WM_POWERBROADCAST suspend and resume codes are broadcast messages that a message-only window never receives, so the
/// window registers with RegisterSuspendResumeNotification, which delivers them to it directly.
/// </remarks>
public sealed class WindowsSessionEvents : ISessionEvents, IDisposable
{
    private const string WindowClassName = "SwaySessionEvents";

    private readonly ManualResetEventSlim _ready = new(false);
    private readonly SessionNative.WindowProc _windowProc;
    private readonly Thread _thread;
    private IntPtr _window;
    private IntPtr _powerSettingHandle;
    private IntPtr _suspendResumeHandle;
    private bool _sessionRegistered;
    private bool _classRegistered;
    private bool _disposed;

    public WindowsSessionEvents()
    {
        _windowProc = WindowProcedure; // kept in a field so the native side never calls a collected delegate
        _thread = new Thread(Run) { IsBackground = true, Name = "Gander session events" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ready.Wait(TimeSpan.FromSeconds(5));
    }

    public event Action<SessionEvent>? Occurred;

    /// <summary>
    /// True when this session is not the active console session (for example another user was switched to) or the
    /// screen is locked. Chosen test for the lock: the input desktop. While locked, Windows switches input to the
    /// "Winlogon" desktop, which a normal process cannot open, so a failed open or a name other than "Default" means
    /// locked. This needs no extra registration and works at launch, before any lock event could have been seen.
    /// </summary>
    public bool IsSessionInactiveNow()
    {
        if (SessionNative.QueryConnectState() is { } state && !SessionMapping.IsActiveConnectState(state))
        {
            return true;
        }

        return SessionMapping.IsLockedDesktop(SessionNative.InputDesktopName());
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_window != IntPtr.Zero)
        {
            SessionNative.PostMessageW(_window, SessionNative.WmClose, IntPtr.Zero, IntPtr.Zero);
        }

        if (_thread.IsAlive && Thread.CurrentThread != _thread)
        {
            _thread.Join(TimeSpan.FromSeconds(5));
        }
    }

    private void Run()
    {
        try
        {
            var instance = SessionNative.GetModuleHandleW(null);
            var windowClass = new SessionNative.WindowClass
            {
                Size = (uint)Marshal.SizeOf<SessionNative.WindowClass>(),
                WindowProc = Marshal.GetFunctionPointerForDelegate(_windowProc),
                Instance = instance,
                ClassName = WindowClassName,
            };
            _classRegistered = SessionNative.RegisterClassExW(ref windowClass) != 0;
            _window = SessionNative.CreateWindowExW(0, WindowClassName, null, 0, 0, 0, 0, 0, (IntPtr)SessionNative.HwndMessage, IntPtr.Zero, instance, IntPtr.Zero);
            if (_window != IntPtr.Zero)
            {
                _sessionRegistered = SessionNative.WTSRegisterSessionNotification(_window, SessionNative.NotifyForThisSession);
                var displayState = SessionMapping.ConsoleDisplayState;
                _powerSettingHandle = SessionNative.RegisterPowerSettingNotification(_window, ref displayState, SessionNative.DeviceNotifyWindowHandle);
                _suspendResumeHandle = SessionNative.RegisterSuspendResumeNotification(_window, SessionNative.DeviceNotifyWindowHandle);
            }
        }
        catch (Exception)
        {
            // No events is the worst case; the controller still seeds its state from IsSessionInactiveNow.
        }
        finally
        {
            _ready.Set();
        }

        if (_window == IntPtr.Zero)
        {
            return;
        }

        try
        {
            while (SessionNative.GetMessageW(out var message, IntPtr.Zero, 0, 0) > 0)
            {
                SessionNative.TranslateMessage(ref message);
                SessionNative.DispatchMessageW(ref message);
            }
        }
        catch (Exception exception) when (exception is not (OutOfMemoryException or AccessViolationException))
        {
            // A stray exception here would end the process; the window procedure already guards its own work.
            System.Diagnostics.Trace.TraceError($"Gander session events loop ended: {exception.GetType().Name}");
        }

        if (_classRegistered)
        {
            SessionNative.UnregisterClassW(WindowClassName, SessionNative.GetModuleHandleW(null));
        }
    }

    private IntPtr WindowProcedure(IntPtr window, uint message, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            switch (message)
            {
                case SessionNative.WmWtsSessionChange:
                    Raise(SessionMapping.FromSessionChange((int)wParam));
                    return IntPtr.Zero;
                case SessionNative.WmPowerBroadcast:
                    HandlePowerBroadcast((int)wParam, lParam);
                    return (IntPtr)1; // TRUE: the message was handled
                case SessionNative.WmClose:
                    SessionNative.DestroyWindow(window);
                    return IntPtr.Zero;
                case SessionNative.WmDestroy:
                    Unregister(window);
                    SessionNative.PostQuitMessage(0);
                    return IntPtr.Zero;
            }
        }
        catch (Exception)
        {
            // A failing handler must never take the message loop down.
        }

        return SessionNative.DefWindowProcW(window, message, wParam, lParam);
    }

    private void HandlePowerBroadcast(int code, IntPtr lParam)
    {
        if (code == SessionNative.PbtPowerSettingChange && lParam != IntPtr.Zero)
        {
            // POWERBROADCAST_SETTING: Guid PowerSetting (16 bytes), uint DataLength, byte Data[DataLength].
            var setting = Marshal.PtrToStructure<Guid>(lParam);
            var length = Marshal.ReadInt32(lParam, 16);
            if (length is > 0 and <= 16)
            {
                var data = new byte[length];
                Marshal.Copy(lParam + 20, data, 0, length);
                Raise(SessionMapping.FromPowerSetting(setting, data));
            }

            return;
        }

        Raise(SessionMapping.FromPowerBroadcast(code));
    }

    private void Raise(SessionEvent? sessionEvent)
    {
        if (sessionEvent is { } value)
        {
            Occurred?.Invoke(value);
        }
    }

    private void Unregister(IntPtr window)
    {
        if (_powerSettingHandle != IntPtr.Zero)
        {
            SessionNative.UnregisterPowerSettingNotification(_powerSettingHandle);
            _powerSettingHandle = IntPtr.Zero;
        }

        if (_suspendResumeHandle != IntPtr.Zero)
        {
            SessionNative.UnregisterSuspendResumeNotification(_suspendResumeHandle);
            _suspendResumeHandle = IntPtr.Zero;
        }

        if (_sessionRegistered)
        {
            SessionNative.WTSUnRegisterSessionNotification(window);
            _sessionRegistered = false;
        }
    }
}
