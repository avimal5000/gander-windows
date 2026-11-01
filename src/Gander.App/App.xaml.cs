using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Gander.App.Hosting;
using Gander.App.Presentation;
using Gander.App.Tray;
using Gander.App.Windows;
using Gander.Core.Calibration;
using Gander.Core.Controller;
using Gander.Core.Platform;
using Gander.Core.Runtime;
using Gander.Platform.Windows;
using Gander.Platform.Windows.Displays;
using Gander.Platform.Windows.Focus;
using Gander.Platform.Windows.Hotkeys;
using Gander.Platform.Windows.Settings;
using Gander.Platform.Windows.Windowing;

namespace Gander.App;

/// <summary>
/// The tray app: the composition root. It builds the Windows adapters, hands them to the
/// <see cref="TrackingController"/>, and shows the controller's state in the tray. Everything runs on the WPF
/// dispatcher thread; the controller posts back to it through <see cref="DispatcherMainThread"/>.
/// </summary>
public partial class App : Application
{
    private const string MutexName = @"Local\Gander.TrayApp.SingleInstance";

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
    private PoseDebugWindow? _poseDebug;
    private GateTestWindow? _gateTest;
    private FocusTestWindow? _focusTest;
    private CameraDemand? _cameraDemand;
    private IFocusController? _focus;
    private Func<double>? _clock;
    private SettingsWindow? _settingsWindow;
    private SetupWindow? _calibration;
    private SetupSession? _setup;
    private RegistryLoginItem? _loginItem;
    private PromptOverlayHost? _overlay;
    private FeedbackHost? _feedback;
    private AppPreferences? _preferences;
    private bool _quitting;

    /// <summary>Opens (or raises) the Set up Gander window.</summary>
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
        _preferences?.MarkOnboardingSeen();
    }

    // First run: the walkthrough opens by itself once, in front of the other windows, when nothing is calibrated.
    private void OpenFirstRunSetup()
    {
        if (_controller is null || _preferences is null
            || !FirstRunRule.ShouldOpenSetup(_preferences.OnboardingSeen, _controller.Library.Profiles.Count))
        {
            return;
        }

        OpenCalibration();
        if (_calibration is { } window)
        {
            // Topmost for a moment so a window opened from the startup shortcut is not left behind another app.
            window.Topmost = true;
            window.Activate();
            window.Topmost = false;
        }
    }

    /// <summary>Opens (or raises) the Settings window.</summary>
    public void OpenSettings()
    {
        if (_controller is null || _loginItem is null || _preferences is null)
        {
            return;
        }

        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(_controller, _loginItem, OpenCalibration, _preferences!);
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

    /// <summary>Opens (or raises) the Pose Debug window.</summary>
    public void OpenPoseDebug()
    {
        if (_controller is null || _cameraDemand is null || _clock is null)
        {
            return;
        }

        if (_poseDebug is null)
        {
            _poseDebug = new PoseDebugWindow(_controller, _cameraDemand, new TraceStore(), () => DateTimeOffset.Now);
            Track(_poseDebug, "pose-debug");
            _poseDebug.Closed += (_, _) => _poseDebug = null;
        }

        _poseDebug.Show();
        _poseDebug.Activate();
    }

    /// <summary>Opens (or raises) the Head-Pose Gate Test window.</summary>
    public void OpenGateTest()
    {
        if (_controller is null || _cameraDemand is null || _clock is null)
        {
            return;
        }

        if (_gateTest is null)
        {
            _gateTest = new GateTestWindow(_controller, _cameraDemand, _clock, () => DateTimeOffset.Now);
            Track(_gateTest, "gate-test");
            _gateTest.Closed += (_, _) => _gateTest = null;
        }

        _gateTest.Show();
        _gateTest.Activate();
    }

    /// <summary>Opens (or raises) the Focus Test window.</summary>
    public void OpenFocusTest()
    {
        if (_controller is null || _focus is null || _clock is null)
        {
            return;
        }

        if (_focusTest is null)
        {
            _focusTest = new FocusTestWindow(_controller, _focus, _clock, () => DateTimeOffset.Now);
            Track(_focusTest, "focus-test");
            _focusTest.Closed += (_, _) => _focusTest = null;
        }

        _focusTest.Show();
        _focusTest.Activate();
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
            // Another Gander is already in the tray. Leave quietly.
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
        _log.Write($"Gander {SystemInfo.GanderVersion()} starting on {SystemInfo.WindowsVersion()}");

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
        _feedback?.Dispose();
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
        EarlierNameMigration.Run();
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
        _clock = environment.Clock;
        _focus = focus;
        _cameraDemand = new CameraDemand(
            _controller.SetCalibrationRunningAsync,
            () => _controller.TrackingEnabled && _controller.ActiveCalibration is not null && _controller.Layout.Displays.Count >= 2,
            () => _controller.CalibrationRunning && _cameraDemand?.Holding != true);

        _setup = new SetupSession(_controller, environment.MainThread, environment.Scheduler, environment.Clock);
        _overlay = new PromptOverlayHost(_setup.Calibration);

        _preferences = new AppPreferences(_settings);
        AppTheme.Preferences = _preferences;
        _feedback = new FeedbackHost(_controller, _preferences);

        _tray = new TrayHost(
            _controller,
            OpenCalibration,
            OpenSettings,
            OpenDiagnostics,
            OpenPoseDebug,
            OpenGateTest,
            OpenFocusTest,
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

        // First launch: nothing calibrated and the walkthrough not yet seen. Runs after the tray icon exists.
        Dispatcher.BeginInvoke(OpenFirstRunSetup);
    }

    private void Track(Window window, string name)
    {
        window.Activated += (_, _) => SetInFront(name, true);
        window.Deactivated += (_, _) => SetInFront(name, false);
        window.Closed += (_, _) => SetInFront(name, false);
    }

    private void SetInFront(string source, bool inFront) => _controller?.SetGanderInFront(_inFront.Set(source, inFront));

    private string BuildDiagnostics()
    {
        var snapshot = DiagnosticsSnapshotBuilder.Build(
            _controller!,
            _settings!,
            display => NativeShell.ScalePercentAt(
                (int)(display.Frame.X + display.Frame.Width / 2),
                (int)(display.Frame.Y + display.Frame.Height / 2)),
            SystemInfo.GanderVersion(),
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
