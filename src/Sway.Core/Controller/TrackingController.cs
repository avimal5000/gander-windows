using Sway.Core.Platform;
using Sway.Core.Runtime;

namespace Sway.Core.Controller;

/// <summary>
/// The tracking brain of Sway for Windows, a UI-free port of the Mac <c>AppModel</c> (tracking, camera lifecycle, holds,
/// Focus Lock, calibration library, camera fallback). It owns the camera lifecycle (the pose source runs only while
/// Tracking is on and a calibration fits the current layout), runs every pose sample through the switch decider, and
/// moves focus when a look is held. State is exposed as read-only properties plus one <see cref="StateChanged"/> event.
/// <para>
/// Threading: create it and call its methods on the main thread (a thread with a <see cref="SynchronizationContext"/>
/// in the app, so awaits resume there). Platform events and the pose stream arrive on any thread and are handed to
/// <see cref="IMainThread.Post"/>. <see cref="StateChanged"/> is raised on the main thread, only when something the
/// UI shows has changed, never at the frame rate.
/// </para>
/// The logic is split across partial files by concern, like the Swift extensions.
/// </summary>
public sealed partial class TrackingController : IDisposable
{
    private readonly TrackingEnvironment _env;
    private readonly IDelayScheduler _scheduler;
    private readonly ChangeHolds _holds;
    private readonly SuspendTracker _suspend = new();
    private readonly RetryBackoff _backoff = new();
    private readonly TrackingLoop _loop = new();
    private readonly FaceDebounce _debounce = new();
    private readonly List<string> _logLines = [];
    private readonly Dictionary<string, string> _knownCameraNames = [];
    private Dictionary<DisplayId, string> _displayLabels = [];
    private bool _disposed;
    private object? _lastPublished;

    /// <summary>Creates the controller, reads the saved choices, and subscribes to the platform events.</summary>
    public TrackingController(TrackingEnvironment environment)
    {
        _env = environment ?? throw new ArgumentNullException(nameof(environment));
        Authorization = _env.Cameras.Authorization();
        SelectedCameraId = _env.Settings.Read(SettingsKeys.CameraDeviceId);
        Layout = _env.Displays.Current();
        Devices = _env.Cameras.AvailableDevices();
        PredictedCameraName = _env.Cameras.Resolve(SelectedCameraId)?.Name;
        Library = StoredLibrary();
        Sensitivity = StoredSensitivity();
        FocusLockShortcut = StoredFocusLockShortcut();
        TrackingEnabled = _env.Settings.Read(SettingsKeys.TrackingEnabled) == "true";
        _displayLabels = DisplayLabeler.Labels(Layout);
        _enterMargin = Sensitivity.ToSwitchSettings().EnterMargin;
        _scheduler = new MainThreadScheduler(_env.Scheduler, _env.MainThread, Publish);
        _holds = new ChangeHolds(_scheduler, _env.Clock, Log);
        _holds.DisplayChangeAccepted += ResetTracking;
        _holds.SpaceChangeNotified += CancelFocusPoll;
        _holds.HoldEnded += (_, _) =>
        {
            if (_isPollingFocus)
            {
                PollFocusedDisplay();
            }
        };

        if (_env.Session.IsSessionInactiveNow())
        {
            _suspend.Begin(SuspendReason.SessionInactive);
        }

        Log(SensitivityLogLine(Sensitivity));
        RememberCameraNames();
        RefreshActiveCalibration(reconcile: false);
        _env.Displays.LayoutChanged += OnLayoutChanged;
        _env.Cameras.DevicesChanged += OnDevicesChanged;
        _env.Session.Occurred += OnSessionEvent;
        RegisterFocusLockShortcut();
        if (TrackingEnabled)
        {
            // Tracking starts without any window: one reconcile right after launch.
            _env.MainThread.Post(() =>
            {
                _ = ReconcileCamera();
                Publish();
            });
        }
    }

    /// <summary>
    /// Raised on the main thread after something the UI shows changed: the status, Tracking, Focus Lock, the shortcut
    /// message, the camera choice, the settings, the library, and so on. Not raised for per-frame values.
    /// </summary>
    public event Action? StateChanged;

    /// <summary>Raised on the main thread when a switch really moved focus (the UI may flash the edge glow).</summary>
    public event Action<DisplayId>? FocusSwitched;

    /// <summary>Raised on the main thread when the Focus Lock shortcut was pressed (the UI may show a notice, SPEC F28).</summary>
    public event Action<bool>? FocusLockToggledByShortcut;

    // MARK: Observable state

    /// <summary>Latest lifecycle state of the camera session.</summary>
    public CaptureStatus Status { get; private set; } = new CaptureStatus.Stopped();

    /// <summary>Camera permission as last read.</summary>
    public CameraAuthorization Authorization { get; private set; }

    /// <summary>Connected cameras, refreshed when a camera connects or disconnects.</summary>
    public IReadOnlyList<CameraDevice> Devices { get; private set; }

    /// <summary>The camera capture would open for <see cref="SelectedCameraId"/> right now.</summary>
    public string? PredictedCameraName { get; private set; }

    /// <summary>The user's preferred camera; null means the default (built-in first). Saved.</summary>
    public string? SelectedCameraId { get; private set; }

    /// <summary>
    /// The Tracking switch: on means tracking, off is Paused with the camera off (SPEC D26). Only the user's own
    /// choices are saved; a camera failure, a lost camera or quitting turns it off in memory alone.
    /// </summary>
    public bool TrackingEnabled { get; private set; }

    /// <summary>Name of the camera most recently reported as running.</summary>
    public string? RunningCameraName { get; private set; }

    /// <summary>The camera the current session most recently reported as running; null once the session ends.</summary>
    public CameraDevice? RunningDevice { get; private set; }

    /// <summary>The connected camera used while the chosen one is unplugged (SPEC D46); never saved.</summary>
    public CameraDevice? FallbackCamera { get; private set; }

    /// <summary>The chosen camera that went missing and left no calibrated camera, so Tracking was turned off in memory (SPEC D46).</summary>
    public string? LostCameraId { get; private set; }

    /// <summary>Why the camera cannot be used (denied or restricted), or null.</summary>
    public CameraPermissionPrompt? PermissionPrompt { get; private set; }

    /// <summary>True when a face is seen, after the 0.5 s no-face debounce.</summary>
    public bool HasFace { get; private set; }

    /// <summary>The current display arrangement.</summary>
    public DisplayLayout Layout { get; private set; }

    /// <summary>Every saved calibration. Replaced (never mutated) when it changes.</summary>
    public CalibrationLibrary Library { get; private set; }

    /// <summary>The calibration for the connected displays and camera, restricted to the connected displays; null when none fits (SPEC D25).</summary>
    public CalibrationMatch? ActiveCalibration { get; private set; }

    /// <summary>The user's switching settings (SPEC D31, D49).</summary>
    public SwitchSensitivity Sensitivity { get; private set; }

    /// <summary>The decider's output for the latest tracked sample; null before the first sample or after a reset.</summary>
    public SwitchDecision? LastDecision { get; private set; }

    /// <summary>What the tracking loop believes has keyboard focus.</summary>
    public DisplayId? TrackingFocus => _loop.FocusedDisplay;

    /// <summary>True while Sway's own focus request runs.</summary>
    public bool IsSwitching => _loop.IsFocusing;

    /// <summary>Sway's tray menu or one of its windows is in front (SPEC D28); set by the UI.</summary>
    public bool SwayInFront { get; private set; }

    /// <summary>Focus Lock (SPEC D27): the camera stays on and no switch fires. Not saved; off at launch.</summary>
    public bool FocusLocked { get; private set; }

    /// <summary>The global shortcut that toggles Focus Lock; null means none.</summary>
    public HotKeyShortcut? FocusLockShortcut { get; private set; }

    /// <summary>Why the shortcut could not be registered, or null. Surfaced as state, never thrown.</summary>
    public string? ShortcutMessage { get; private set; }

    /// <summary>True while <see cref="FocusLockShortcut"/> is registered with the system.</summary>
    public bool IsShortcutRegistered { get; private set; }

    /// <summary>A calibration run (a future UI) has the camera: it runs regardless of Tracking and keeps the loop out.</summary>
    public bool CalibrationRunning { get; private set; }

    /// <summary>The last real switches, newest first, at most <see cref="SwitchLogLimit"/>.</summary>
    public IReadOnlyList<SwitchLogEntry> SwitchLog { get; private set; } = [];

    /// <summary>Lines the controller logged (kept for the diagnostics view and the tests).</summary>
    public IReadOnlyList<string> LogLines => _logLines;

    // MARK: Derived state

    /// <summary>True when the camera session exists (running, starting, suspended, or recovering).</summary>
    public bool IsCameraActive => _suspend.IsCaptureRequested;

    /// <summary>The one status the tray icon and menu show (SPEC F19).</summary>
    public TrackingStatus TrackingStatus => TrackingStatus.Evaluate(new TrackingStatusSnapshot(
        Authorization,
        Status,
        TrackingEnabled,
        CalibrationRunning,
        Layout.Displays.Count,
        ActiveCalibration is not null,
        FocusLocked,
        HasFace,
        DisconnectedCameraName));

    /// <summary>Which tray icon state to show. A function of <see cref="TrackingStatus"/>.</summary>
    public TrayIconState TrayIcon => TrackingStatus.Icon;

    /// <summary>Name of the camera in use or about to be used.</summary>
    public string? CurrentCameraName =>
        (Status as CaptureStatus.Running)?.Device?.Name ?? RunningCameraName ?? PredictedCameraName;

    /// <summary>The status text, plus " &#183; &lt;camera name&gt;" while the camera runs.</summary>
    public string StatusLine
    {
        get
        {
            var line = TrackingStatus.Line;
            if (Status is CaptureStatus.Running running && (running.Device?.Name ?? CurrentCameraName) is { } name)
            {
                return $"{line} · {name}";
            }

            return line;
        }
    }

    /// <summary>Why automatic switching is held right now (SPEC D44), if it is.</summary>
    public TrackingHold? TrackingHold => TrackingHolds.Resolve(HoldInputs());

    /// <summary>True while a display change holds switching.</summary>
    public bool DisplaysChanging => _holds.DisplaysChanging;

    /// <summary>True while a virtual desktop change holds switching.</summary>
    public bool DesktopChanging => _holds.SpaceChanging;

    /// <summary>
    /// True while samples feed the decider: Tracking is on, no calibration run steers the camera, and a calibration fits.
    /// </summary>
    public bool IsTrackingActive => TrackingEnabled && !CalibrationRunning && ActiveCalibration is not null && _decider is not null;

    /// <summary>True when a calibration run needs frames, or Tracking is on with a calibration that fits two or more displays.</summary>
    public bool WantsCamera =>
        CalibrationRunning || (TrackingEnabled && ActiveCalibration is not null && Layout.Displays.Count >= 2);

    /// <summary>The unique label of display <paramref name="id"/> in the current layout.</summary>
    public string DisplayName(DisplayId id) => _displayLabels.TryGetValue(id, out var name) ? name : "Unknown display";

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _env.Displays.LayoutChanged -= OnLayoutChanged;
        _env.Cameras.DevicesChanged -= OnDevicesChanged;
        _env.Session.Occurred -= OnSessionEvent;
        CancelPendingTimers();
        _pollTimer?.Cancel();
        _pollCts?.Cancel();
        _switchCts?.Cancel();
    }

    // MARK: Internals

    private TrackingHoldInputs HoldInputs() => new(
        FocusLocked: FocusLocked,
        SwayInFront: SwayInFront,
        DisplaysChanging: _holds.DisplaysChanging,
        SpaceChanging: _holds.SpaceChanging,
        Switching: _loop.IsFocusing);

    private void Log(string line)
    {
        _logLines.Add(line);
        if (_logLines.Count > 200)
        {
            _logLines.RemoveAt(0);
        }

        _env.Log?.Invoke(line);
    }

    /// <summary>Raises <see cref="StateChanged"/> when something the UI shows differs from the last time.</summary>
    private void Publish()
    {
        if (_disposed)
        {
            return;
        }

        var snapshot = (
            TrackingStatus,
            StatusLine,
            TrackingEnabled,
            FocusLocked,
            SwayInFront,
            ShortcutMessage,
            IsShortcutRegistered,
            FocusLockShortcut,
            SelectedCameraId,
            PermissionPrompt,
            Sensitivity,
            Library,
            ActiveCalibration,
            Devices,
            Layout,
            FallbackCamera,
            LostCameraId,
            SwitchLog,
            Hold: TrackingHold);
        if (Equals(snapshot, _lastPublished))
        {
            return;
        }

        _lastPublished = snapshot;
        StateChanged?.Invoke();
    }

    private void Post(Action action) =>
        _env.MainThread.Post(() =>
        {
            if (_disposed)
            {
                return;
            }

            action();
            Publish();
        });
}
