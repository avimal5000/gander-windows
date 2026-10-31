using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Sway.App.Hosting;
using Sway.App.Presentation;
using Sway.App.Tray;
using Sway.App.Windows;
using Sway.Core.Calibration;
using Sway.Core.Controller;
using Sway.Core.Runtime;
using Sway.Platform.Windows;
using Sway.Platform.Windows.Displays;
using Sway.Platform.Windows.Focus;
using Sway.Platform.Windows.Hotkeys;
using Sway.Platform.Windows.Settings;
using Sway.Platform.Windows.Windowing;

namespace Sway.App;

/// <summary>
/// The tray app: the composition root. It builds the Windows adapters, hands them to the
/// <see cref="TrackingController"/>, and shows the controller's state in the tray. Everything runs on the WPF
/// dispatcher thread; the controller posts back to it through <see cref="DispatcherMainThread"/>.
/// </summary>
public partial class App : Application
{
    private const string MutexName = @"Local\Sway.TrayApp.SingleInstance";

    private readonly RotatingFileLog _log = new(RotatingFileLog.DefaultDirectory);
    private readonly InFrontTracker _inFront = new();
    private readonly List<IDisposable> _adapters = [];
    private Mutex? _instance;
    private TrackingController? _controller;
    private TrayHost? _tray;
    private ForegroundWatcher? _foreground;
    private VirtualDesktopWatcher? _desktops;
    private WindowsCameraCatalog? _cameras;
    private JsonFileSettingsStore? _settings;
    private DiagnosticsWindow? _diagnostics;
    private SettingsWindow? _settingsWindow;
    private SetupWindow? _calibration;
    private SetupSession? _setup;
    private RegistryLoginItem? _loginItem;
    private PromptOverlayHost? _overlay;
    private bool _quitting;

    /// <summary>Opens (or raises) the Set up Sway window.</summary>
    public void OpenCalibration()
    {
        if (_setup is null)
        {
            return;
        }

        if (_calibration is null)
        {
            _calibration = new SetupWindow(_setup);
            Track(_calibration, "setup");
            _calibration.Closed += (_, _) => _calibration = null;
        }

        _calibration.Show();
        _calibration.Activate();
    }

    /// <summary>Opens (or raises) the Settings window.</summary>
    public void OpenSettings()
    {
        if (_controller is null || _loginItem is null)
        {
            return;
        }

        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(_controller, _loginItem, OpenCalibration);
            Track(_settingsWindow, "settings");
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        }

        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    /// <summary>Opens (or raises) the diagnostics window.</summary>
    public void OpenDiagnostics()
    {
        if (_controller is null || _settings is null)
        {
            return;
        }

        if (_diagnostics is null)
        {
            _diagnostics = new DiagnosticsWindow(_controller, BuildDiagnostics);
            Track(_diagnostics, "diagnostics");
            _diagnostics.Closed += (_, _) => _diagnostics = null;
        }

        _diagnostics.Show();
        _diagnostics.Activate();
    }

    /// <summary>Quits: turns the camera off first, then ends the process.</summary>
    public async Task QuitAsync()
    {
        if (_quitting)
        {
            return;
        }

        _quitting = true;
        await ShutDownControllerAsync();
        Shutdown();
    }

    /// <inheritdoc />
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _instance = new Mutex(true, MutexName, out var first);
        if (!first)
        {
            // Another Sway is already in the tray. Leave quietly.
            _instance.Dispose();
            _instance = null;
            _quitting = true;
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnDispatcherException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => _log.Write($"Unhandled exception: {args.ExceptionObject}");
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            _log.Write($"Unobserved task exception: {args.Exception}");
            args.SetObserved();
        };
        SessionEnding += OnSessionEnding;

        // The controller captures this context for its awaits.
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher));
        _log.Write($"Sway {SystemInfo.SwayVersion()} starting on {SystemInfo.WindowsVersion()}");

        try
        {
            Compose();
        }
        catch (Exception exception)
        {
            _log.Write($"Start failed: {exception}");
            _quitting = true;
            Shutdown();
        }
    }

    /// <inheritdoc />
    protected override void OnExit(ExitEventArgs e)
    {
        _foreground?.Dispose();
        _desktops?.Dispose();
        _overlay?.Dispose();
        _setup?.Dispose();
        _tray?.Dispose();
        _controller?.Dispose();
        foreach (var adapter in _adapters)
        {
            try
            {
                adapter.Dispose();
            }
            catch (Exception exception)
            {
                _log.Write($"Dispose failed: {exception.Message}");
            }
        }

        if (_instance is not null)
        {
            try
            {
                _instance.ReleaseMutex();
            }
            catch (ApplicationException)
            {
            }

            _instance.Dispose();
        }

        base.OnExit(e);
    }

    private void Compose()
    {
        var clock = Stopwatch.StartNew();
        _settings = new JsonFileSettingsStore();
        _cameras = new WindowsCameraCatalog();
        var poseSource = new WindowsPoseSource(_cameras, null, line => _log.Write(line));
        var displays = new WindowsDisplayCatalog();
        var focus = new WindowsFocusController();
        var hotKeys = new WindowsHotKeyRegistrar();
        var session = new WindowsSessionEvents();
        _adapters.AddRange([poseSource, _cameras, displays, focus, hotKeys, session]);

        var loginItem = _loginItem = new RegistryLoginItem();
        var environment = new TrackingEnvironment
        {
            PoseSource = poseSource,
            Focus = focus,
            Displays = displays,
            Cameras = _cameras,
            Session = session,
            Settings = _settings,
            HotKeys = hotKeys,
            MainThread = new DispatcherMainThread(Dispatcher),
            Scheduler = new TimerDelayScheduler(),
            Clock = () => clock.Elapsed.TotalSeconds,
            Log = _log.Write,
        };
        _controller = new TrackingController(environment);

        _setup = new SetupSession(_controller, environment.MainThread, environment.Scheduler, environment.Clock);
        _overlay = new PromptOverlayHost(_setup.Calibration);

        _tray = new TrayHost(
            _controller,
            loginItem,
            OpenCalibration,
            OpenSettings,
            OpenDiagnostics,
            () => _ = QuitAsync(),
            open => SetInFront("menu", open),
            _log.Write);

        // The controller's own timer polls the focused display once a second; this only makes a change show up sooner.
        _foreground = new ForegroundWatcher(() => _controller?.ForegroundChanged());
        if (!_foreground.IsActive)
        {
            _log.Write("The foreground change hook was refused; the one-second poll covers it.");
        }

        // A virtual desktop switch settles for 0.6 s before switching resumes. The watcher raises on its own thread.
        _desktops = new VirtualDesktopWatcher(line => _log.Write(line));
        _desktops.Changed += () => Dispatcher.BeginInvoke(() => _controller?.ActiveDesktopChanged());

        // The tray shows the latest state when a switch lands or the Focus Lock shortcut is pressed.
        _controller.FocusSwitched += _ => _tray?.Refresh();
        _controller.FocusLockToggledByShortcut += locked =>
        {
            _log.Write($"Focus Lock turned {(locked ? "on" : "off")} by the shortcut");
            _tray?.Refresh();
        };

        // First launch: no saved choice and no calibration yet, so start with the setup window.
        if (_settings.Read(SettingsKeys.TrackingEnabled) is null && _controller.Library.Profiles.Count == 0)
        {
            Dispatcher.BeginInvoke(OpenCalibration);
        }
    }

    private void Track(Window window, string name)
    {
        window.Activated += (_, _) => SetInFront(name, true);
        window.Deactivated += (_, _) => SetInFront(name, false);
        window.Closed += (_, _) => SetInFront(name, false);
    }

    private void SetInFront(string source, bool inFront) => _controller?.SetSwayInFront(_inFront.Set(source, inFront));

    private string BuildDiagnostics()
    {
        var snapshot = DiagnosticsSnapshotBuilder.Build(
            _controller!,
            _settings!,
            display => NativeShell.ScalePercentAt(
                (int)(display.Frame.X + display.Frame.Width / 2),
                (int)(display.Frame.Y + display.Frame.Height / 2)),
            SystemInfo.SwayVersion(),
            SystemInfo.WindowsVersion(),
            SystemInfo.Architecture(),
            DateTimeOffset.UtcNow);
        return DiagnosticsText.Build(snapshot);
    }

    private async Task ShutDownControllerAsync()
    {
        if (_controller is null)
        {
            return;
        }

        try
        {
            // Wait for the camera to be released, but never hang the exit.
            var shutdown = _controller.ShutdownAsync();
            await Task.WhenAny(shutdown, Task.Delay(TimeSpan.FromSeconds(5)));
        }
        catch (Exception exception)
        {
            _log.Write($"Shutdown failed: {exception}");
        }
    }

    // Logoff or shutdown: release the camera before Windows ends the process. The wait pumps the dispatcher so the
    // controller's continuations can run.
    private void OnSessionEnding(object sender, SessionEndingCancelEventArgs e)
    {
        if (_quitting)
        {
            return;
        }

        _quitting = true;
        var done = ShutDownControllerAsync();
        var frame = new DispatcherFrame();
        _ = done.ContinueWith(_ => frame.Continue = false, TaskScheduler.Default);
        if (!done.IsCompleted)
        {
            Dispatcher.PushFrame(frame);
        }
    }

    private void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _log.Write($"Unhandled UI exception: {e.Exception}");

        // Keep the tray alive unless the process is in no state to continue.
        e.Handled = e.Exception is not (OutOfMemoryException or StackOverflowException or AccessViolationException);
    }
}
