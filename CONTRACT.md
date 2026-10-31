# Sway for Windows: Contract

Code-level interfaces and rules. Each milestone adds its section here before a builder starts on it. The Mac app's
`CONTRACT.md` is the reference for the engine sections (calibration, classifier, decider, sensitivity); they are ported
with golden vectors (PLAN.md section 4), not reinterpreted.

## 1. Conventions

- **Angles** are degrees everywhere in `Sway.Core`. A pose source that works in radians converts at its own edge.
- **Projects.** `Sway.Core` (net8.0, portable) never references a Windows API or a type from `Sway.Platform.Windows`.
  Windows behavior sits behind interfaces that Core defines (`IPoseSource`, `IFocusPlatform`, `IDisplayCatalog`,
  `ISessionEvents`).
- **Time.** Anything that waits takes an injected scheduler or clock, so tests never sleep.
- **Errors.** A failure that makes Sway unable to track is shown in the tray status and logged; it never throws past a
  module boundary.
- **Style.** `dotnet format` clean, nullable on, warnings as errors, file-scoped namespaces, `_camelCase` private
  fields.
- **Pose sign convention** (yaw, pitch, roll zero and direction) is fixed in W2, before any estimator is written, and
  copied from the Mac app's recorded traces so they replay unchanged.

## W2 runtime pieces (portable, `Sway.Core.Runtime`)

Pure logic ported from the Mac app with its tests (CONTRACT 6.1.1, 6.2.1, 6.5.4 to 6.5.6; SPEC D43 to D46, D49). None
reads a clock or sleeps: time comes in as arguments or through `IDelayScheduler`.

- **`IDelayScheduler`** `IScheduledAction Schedule(double delaySeconds, Action work)`; `IScheduledAction.Cancel()`
  (safe after it ran or twice). `TimerDelayScheduler` is the real one (thread-pool timer; the caller marshals to the UI
  thread). Tests use `ManualDelayScheduler` (tests project): `Advance`, `PendingDelays`, `HasPending`.
- **`SuspendReason`** `SystemSleep`, `DisplaysAsleep`, `ScreenLocked`, `SessionInactive`.
- **`SuspendTracker`** reason-set state machine, no I/O. `CaptureRequested()`, `CaptureStopped()`, `Begin(reason)`,
  `End(reason)`, `End(reasons)`, `Apply(SuspendEvent, Func<bool> isMainDisplayAsleep)` (self-heals a missed
  display-on), `Reasons`, `FirstReason`, `IsSuspended`, `IsCaptureRequested`, `Clone()`. Each returns a
  `SuspendDirective` (`None`, `Suspend(reason)`, `ReasonChanged(reason)`, `Resume`). Reasons are sticky for the
  controller's lifetime: recorded and cleared whether or not capture is requested, kept by `CaptureStopped()`; nothing
  resumes after stop. Lock and unlock are fed in the order they arrive, immediately.
- **`RetryBackoff`** `NextOpenFailureDelay()` (1, 2, 5, then 10 s), `NextStallRebuildDelay()` (10, 30, then 60 s),
  `FramesFlowed()` resets both.
- **`PoseDemand`** `Full`, `Adaptive`, `Reduced`. **`PoseDemandPolicy.Wanted(PoseDemandInputs)`**: Full if the gate
  test, Setup's camera check, Pose Debug, or the Focus Test window is live; else Reduced for Focus Lock or
  `SwitchingUnavailable`; else Adaptive.
- **`AdaptiveFrameRate`** `ShouldProcess(t)`, `Record(face, t)`, `MinimumInterval(t)`, `SetDemand`,
  `SetPowerConstrained` (battery saver or thermal), `Reset()`. Slot schedule: process when
  `t >= slot + interval - 0.004`, then `slot = max(slot + interval, t - interval / 2)`. 15 fps cap; Adaptive drops to
  5 fps (2 fps constrained) after 2 s without a face; Reduced is 5 fps (2 constrained); Adaptive constrained with a
  face is 10 fps.
- **`TrackingHold`** `FocusLock`, `SwayInFront`, `FocusTest`, `DisplaysChanging`, `SpaceChanging`, `Switching`.
  `TrackingHolds.Resolve(TrackingHoldInputs)` returns the first that applies in that order (null if none);
  `IsLocked` is true when any applies and is what the decider is told. `Text()` is the Pose Debug wording ("Sway's
  menu or window is in front", "Virtual desktop changing..." are the Windows wording). Constants
  `DisplaysChangingTimeout` 0.75 s, `DisplaysChangingCap` 3 s, `SpaceChangingDuration` 0.6 s.
- **`ChangeHolds`** the timing for the two timed holds: `DisplayParametersDidChange()`, `EndDisplaysChanging(HoldEnd)`
  (call with `Layout` when the new layout is applied), `ActiveSpaceDidChange()`, `EndSpaceChanging()`, `CancelAll()`;
  `DisplaysChanging`/`SpaceChanging`; events `DisplayChangeAccepted` (reset the loop), `SpaceChangeNotified` (drop an
  in-flight focus poll), `HoldEnded` (poll focus once). A display hold never lasts past 3 s from its first
  notification; after the cap, chatter is ignored until it pauses for 0.75 s. `HoldLog` has the log wording.

## 2. W2 engine (milestone W2, issues #6 #7 #8 #9)

A port of the Mac decision engine to `Sway.Core` (namespace `Sway.Core`, folder `src/Sway.Core/Engine`). It is pure and
portable: no Windows API, no clock, no I/O. Time comes only from sample timestamps. Names are PascalCase versions of the
Swift names so the two can be compared side by side. `tests/Sway.Core.Tests/Engine` ports every relevant Swift test with
the same inputs and expected numbers; `Fixtures/GateTraceFixture.cs` holds the Mac app's real gate recording
(2026-10-24, C922, 3 displays x 2 rounds x 75 samples) as C# data, because the test project does not copy content files.

### 2.1 Public types

| C# type | Swift original | Rule |
|---|---|---|
| `HeadPose` (extended) | `HeadPose` | Adds `FaceConfidence` (default 1), `LandmarkYaw`, `LandmarkPitch` (default null). The 3-argument form still works. Face bounds are not carried. |
| `TimedPose(Timestamp, Pose?)` | `PoseSample` | One frame; `Pose` null means no face. Named differently because `PoseSample` already exists for traces. |
| `TraceSegment`, `LabeledPoseTrace` | `TraceSegment`, `PoseTrace` | Labeled segments for fitting (display, round, samples), plus layout, camera name, `CreatedAt`. Separate from the flat `PoseTrace`. |
| `DisplayId`, `DisplayRect`, `DisplayInfo`, `DisplayLayout` | `DisplayID`, `DisplayInfo`, `DisplayLayout` | Layout sorts by X, then Y, then id (ordinal). `Fingerprint` is `id@x,y,wxh` of the integral frame, joined by `\|`, built from ids and frames only (never native ids). |
| `PoseFeatureSet`, `FeaturePoint` | same | `VisionAngles` = (Yaw, Pitch); `LandmarkAngles` needs both landmark angles, else the pose is no face. |
| `DisplayProfile`, `CalibrationProfile` | same | Mean and sample (n-1) std per display; `YawScale`/`PitchScale` pooled RMS, floored at 1.5 / 2.5, and `PitchScale >= YawScale`. `Matches(layout)` = same format version and fingerprint. `RestrictedTo(ids)` keeps order, scales and metadata. |
| `CalibrationKey`, `CalibrationMatch`, `CalibrationLibrary` | same | One profile per (display set, camera); newest first; max 24; profiles with fewer than 2 displays or another format are ignored. `Resolve` picks the same camera, displays covering the layout (at least 2), fewest displays, then most recent. JSON decode drops broken or outdated profiles; another library version decodes empty. |
| `SwitchSettings` | `SwitchSettings` | Dwell 0.40 s, enter margin 0.30, stay margin 0.15, max gap 0.25 s, cooldown 0.75 s, min face confidence 0.5, elsewhere margins 20 deg yaw / 15 deg pitch. Immutable record, use `with`. |
| `SwitchSensitivity`, `DoubleRange` | `SwitchSensitivity` | Dwell range 0.05 to 1.5 s, cooldown range 0.2 to 1.5 s, confidence margins 0.15, 0.225, 0.30, 0.375, 0.45 (stay = half). Constructors clamp. `Default` equals `SwitchSettings.Default`; `Instant` is dwell 0.10, cooldown 0.30 (SPEC D49); `ApplyingInstantTiming()` keeps the confidence. JSON: always writes all three fields; a missing `cooldown` reads as 0.75; a missing `dwell` or `confidenceLevel` is a `JsonException`; out-of-range values clamp. |
| `PoseClassifier`, `PoseClassification` | `PoseClassifier`, `PoseClassification` | Nearest calibrated center in axis-scale units (D18). Margin = (d2 - d1) / (d2 + d1), 0 if the sum is 0 or there is one display. Ties keep the earlier display. A pose outside the box (display means extended by the elsewhere margins) is `Elsewhere` with margin 0. No pose, confidence below the minimum, missing features, or no displays is `NoFace`. |
| `ProfileFitter`, `CalibrationException` | `ProfileFitter`, `CalibrationError` | Needs 30 face points per display; fewer than 2 such displays is `TooFewDisplays`; faces without the requested features is `FeaturesMissing`. Display order follows the trace layout, then first appearance. Deterministic. |
| `SwitchStateMachine`, `ISwitchDecider` | `SwitchStateMachine`, `SwitchDeciding` | Steps: 0 backwards time resets; 1 classify; 2 Focus Lock (a `locked` input) cancels everything including a cooldown; 3 cooldown blocks until `t >= until - eps`; 4 an ongoing dwell fires, continues, or ends (focus already there, gap over max gap, or a confident look elsewhere); 5 idle starts a dwell only with a display that is a target, not already focused, with margin at least the enter margin. A dwell is kept alive at the stay margin. Uncertain, elsewhere or no face never switches. `TimeTolerance` (eps) is 1e-6 s. |
| `SwitchContext`, `SwitchPhase` (`IdlePhase`, `DwellingPhase`, `CoolingDownPhase`, `LockedPhase`), `SwitchHoldReason`, `SwitchDecision` | same | `FocusedDisplay` null means unknown (any display may be switched to). `Targets` null means every display. `SwitchTo` is set exactly on the sample that completes a dwell. |
| `PoseSwitchEngine`, `ISwitchEngineMaking` | `PoseSwitchEngine`, `SwitchEngineMaking` | Stateless facade: `FitProfile`, `Classify`, `MakeDecider`. Safe to share across threads; each decider has its own state. |
| `SwayPolicy` | `SwayPolicy` | `MaxTrackedDisplays` = 3, `DefaultFrameRate` = 15. |

### 2.2 Differences from the Mac behavior

None in the decision logic: the same thresholds, order of steps and tolerances, and every ported test uses the Swift
inputs and expected numbers. Representation differences only:

- `DisplayInfo.NativeId` replaces `cgDisplayID`, and `DisplayRect` replaces `CGRect` (pixels, not points). Frames on
  Windows are in physical pixels; the fingerprint format is unchanged.
- `Date` is `DateTimeOffset`; swift enums with payloads (`SwitchPhase`) are records; `Set<DisplayID>?` is `IReadOnlySet<DisplayId>?`.
- JSON uses camelCase property names like the Mac files. `DisplayId` is `{"rawValue": ...}` as on the Mac.
- No logging calls (the Swift code only logs at debug level).

## 3. Platform interfaces (milestones W3 to W5)

Defined in `Sway.Core.Platform` (portable). The Windows adapters live in `Sway.Platform.Windows` and the tests use
fakes. Rules for every adapter:

- **Threads.** Events (`DevicesChanged`, `LayoutChanged`, `SessionEvents.Occurred`, hotkey handlers) may be raised on any
  thread, and more than once for one real change. The tracking controller serializes them; adapters never call back
  into the controller while holding a lock.
- **Camera.** `IPoseSource.StartAsync` is the only thing that turns the camera on, and `StopAsync` (or disposal) turns it
  off and returns only after the capture session is released. A pose sample with a null pose means "no face this frame".
  No frame, bitmap, or pixel leaves the adapter; only `TimedPose` numbers do. Timestamps are seconds on a monotonic
  clock.
- **Capture status.** Reported in order: `Starting`, `Running(device)`, then `Interrupted`, `Suspended(reason)` or
  `Failed(error)`, and `Stopped` after a stop. A camera that disappears reports `Failed(DeviceUnavailable)`.
- **Focus.** `FocusAsync` uses the verified recipe of docs/decisions/02-focus.md (attach to the foreground thread, bring to
  top, set foreground, poll up to 600 ms for the foreground window to equal the target, always detach), never moves or
  reads the cursor in shipped code, and never synthesizes input. Window enumeration follows docs/decisions/03: topmost
  eligible window per display; windows are identified by handle, process id and app name only (no titles).
- **Displays.** Display ids are stable for the same physical monitor across runs (derived from the monitor's device
  path/EDID identity, not the `\\.\DISPLAYn` name, which can change); `DisplayInfo.Name` is the friendly name.
  `DisplayRect` is in physical pixels in virtual-screen coordinates; the process is per-monitor DPI aware.
- **Hotkeys.** `RegisterHotKey` only; no keyboard hook. `Register` throws `HotKeyException`; the caller shows it.
- **Settings.** Plain strings in one JSON file; writes are atomic (write temp, replace). Unknown keys are kept.
- **Privacy.** `scripts/privacy-guard.sh` bans networking, saved images, input synthesis and capture, and cursor APIs in
  `src/`. A needed exception carries a `privacy-guard:allow` comment saying why and is called out in the PR.

## W3 camera and session (issues #11, #12 and the lock/sleep events)

Windows adapters in `Sway.Platform.Windows`; their decision rules are pure and live in `Sway.Core.Platform` with tests in
`tests/Sway.Core.Tests/Platform`.

- **`WindowsCameraCatalog`** `ICameraCatalog`, also `IDisposable`. Devices from `Windows.Devices.Enumeration`
  (`VideoCapture`, enabled only); the id is the device-interface id. `IsBuiltIn` = enclosure panel Front or Back, or a
  name hint (`CameraSelection.IsBuiltIn`). `Resolve` = saved id, else first built-in, else first
  (`CameraSelection.Resolve`). `Authorization()` reads the consent-store `Value` for the user, the machine and
  `NonPackaged` (desktop apps); any `Deny` is `Denied`, otherwise `Authorized` (`CameraSelection.Interpret`).
  `DevicesChanged` comes from a `DeviceWatcher` started on the first subscriber; Added/Removed/Updated all raise it,
  except the Added burst of the first enumeration.
- **`WindowsPoseSource`** `IPoseSource`, also `IDisposable`; constructor `(ICameraCatalog, string? modelPath = null)`
  (model: `models/face_landmark.onnx` under `AppContext.BaseDirectory`). `StartAsync` returns at once; a dedicated
  thread opens the camera. Events go through a bounded channel (64, drop oldest). Order: `Starting`, `Running(device)` at
  the first frame, `Interrupted` (no frame for 3 s, or the capture reported failure; camera released, reopened after
  10, 30, then 60 s, `RetryBackoff`; an open that fails while the camera still exists retries after 1, 2, 5, then 10 s),
  `Failed(error)`, and `Stopped` last. Failures: denied or restricted is `NotAuthorized`; no camera `NoDevice`; the
  chosen camera gone from the catalog `DeviceUnavailable`; a missing or unloadable model `ConfigurationFailed`; the
  model failing on 50 frames in a row `RuntimeFailure`. After `Failed` the stream stays open until `StopAsync`.
  `StopAsync` returns after the camera is released and the stream completed; a second `StartAsync` before it throws
  `InvalidOperationException`. `SetDemand` is honored (`AdaptiveFrameRate`: frames the gate refuses are never
  converted); `SetPowerConstrained(bool)` is extra. `PoseSourceConfiguration.FrameRate` and `IncludeLandmarks` are
  ignored (`HeadPose.LandmarkYaw`/`LandmarkPitch` stay null). Timestamps are seconds on `Stopwatch` time. The sample's
  `HeadPose.FaceConfidence` is the model's face-presence score. The camera is opened with `ExclusiveControl`.
- **`CaptureSupervisor`** (Core, pure) the stall watchdog and retry timing: `Opened`, `OpenFailed` (returns the delay),
  `FrameArrived`, `Interrupt`, `Tick` returning `SupervisorAction` (`None`, `ReportInterrupted`, `Rebuild`).
- **`WindowsSessionEvents`** `ISessionEvents`, also `IDisposable`. A message-only window on its own thread. Mapping
  (`SessionMapping`, pure): lock/unlock to `ScreenLocked`/`ScreenUnlocked`; console disconnect/connect to
  `SessionInactive`/`SessionActive`; `PBT_APMSUSPEND` to `SystemSleeping`; `PBT_APMRESUMEAUTOMATIC` and
  `PBT_APMRESUMESUSPEND` to `SystemWoke` (both arrive on a user wake, so wake can repeat); `GUID_CONSOLE_DISPLAY_STATE`
  0 or 2 to `DisplaysSleeping`, 1 to `DisplaysWoke`. Suspend/resume use `RegisterSuspendResumeNotification` because a
  message-only window never receives the broadcast. Events are raised on the window thread, outside any lock.
  `IsSessionInactiveNow()` is true when the session's WTS connect state is not Active, or the input desktop cannot be
  opened or is not named "Default" (the locked workstation shows "Winlogon"). Dispose unregisters and ends the thread.
- **Spike tools** `SwayTools` reuses the internal `CameraSource`, `LandmarkModel` and `PosePipeline` (moved to
  `Sway.Platform.Windows.Pose`) through `InternalsVisibleTo`.

## W4 focus, displays, hotkeys, settings (issues #13, #14, #15)

Windows adapters in `src/Sway.Platform.Windows`, one folder per area. Win32 calls sit behind small internal interfaces
(`IWindowNativeCalls`, `IDisplayNative`, `IRunKeyAccess`) so the logic is tested with fakes in
`tests/Sway.Platform.Windows.Tests`. `InternalsVisibleTo` is granted to that test project and to `SwayTools`.

- **`WindowsDisplayCatalog : IDisplayCatalog`** (`Displays/`). `Current()` reads the live configuration each call
  (EnumDisplayMonitors for rectangles, QueryDisplayConfig + DisplayConfigGetDeviceInfo for device path, friendly name,
  output technology) and never throws (empty layout on failure). `LayoutChanged` is raised on a pump thread for
  WM_DISPLAYCHANGE and WM_DPICHANGED, possibly repeatedly and without checking the layout really differs. The pump window
  is hidden top-level, not message-only, because message-only windows get no broadcasts. `DisplayId` = lower-cased monitor
  device path without the trailing interface GUID (`\\?\display#gsm5b09#5&2a5e3c3f&0&uid4352`), or `gdi:\\.\display1` when
  Windows gives no path; duplicates get `#2`, `#3` in left-to-right order. `NativeId` = low 32 bits of the HMONITOR.
  `DisplayLayoutBuilder` (pure) builds the `DisplayLayout`; `IsBuiltIn` = output technology internal, DisplayPort-embedded
  or UDI-embedded. Frames are physical pixels only because the app manifest makes the process per-monitor DPI aware.
- **`WindowSnapshot`, `WindowEligibility`, `WindowRejection`** (`Windowing/`, pure). The snapshot has handle, pid, class,
  ex-style, owner, visible, iconic, cloaked, on-current-desktop, rect, monitor handle, process name, elevated; no title.
  `Check` applies, in order: visible, not minimized, not cloaked, not tool-window-or-owned-popup unless `WS_EX_APPWINDOW`,
  not `WS_EX_NOACTIVATE`, not shell chrome class, not Sway's own process, non-empty rect, on the current virtual desktop.
  `LastUsedByDisplay(topmostFirst, ownPid, displayOfMonitor)` returns the first eligible window per display.
  `DisplayMapper.Create(layout, boundsOfMonitor)` maps a monitor handle to a `DisplayId` (by `NativeId`, then by frame).
- **`WindowsFocusController : IFocusController`** (`Focus/`). All native work on one dedicated worker thread
  (`SerialWorker`); tasks complete on the thread pool. `FocusedDisplayAsync` is null for no foreground window, Sway's own
  window, or shell chrome. `FocusAsync`: `NoWindow` when the display has no eligible window; `Focused(window, Plain, 0)`
  when it already is foreground; otherwise restore if minimized, attach to the foreground thread (skipped, method `Plain`,
  when that thread already owns the target), BringWindowToTop, SetForegroundWindow, detach in `finally`, then check the
  foreground at 0, 50 ... 600 ms. `Failed` carries a reason naming the app (never a title); an elevated target with a
  non-elevated Sway says it runs as administrator. Methods never throw. `Dispose()` stops the worker.
- **`WindowsHotKeyRegistrar : IHotKeyRegistrar`** (`Hotkeys/`). RegisterHotKey with `MOD_NOREPEAT` on a dedicated pump
  thread. `Register` is synchronous and throws `HotKeyException`: `InvalidShortcut` (invalid shortcut or error 87),
  `ShortcutInUse` (1409), `RegistrationFailed` (anything else). Registering an action again replaces its shortcut.
  **Handlers run on the pump thread**: the caller marshals to its UI thread. A handler exception is swallowed.
  `HotKeyMapping.ToNative` maps `HotKeyModifiers` (Control = 1, Alt = 2) to Win32 `MOD_` bits (Alt = 1, Control = 2).
- **`JsonFileSettingsStore : ISettingsStore`** (`Settings/`). One JSON object, default `%AppData%\Sway\settings.json`,
  path injectable. Loaded once, kept in memory, every write replaces the file atomically (`.tmp` then replace). Missing
  file = empty. Corrupt file (not JSON, or not an object) = empty plus a copy at `settings.json.bak`; the next write
  replaces it. Unknown keys and non-string values are preserved (a non-string reads back as its JSON text). Thread-safe.
  A failed write keeps the value in memory and sets `LastWriteError`; it does not throw.
- **`RegistryLoginItem : ILoginItem`** (`Settings/`). HKCU `...\CurrentVersion\Run`, value `Sway` = the quoted exe path
  (`Environment.ProcessPath`). Registry access is the `IRunKeyAccess` seam. `Status` is `Enabled` only if the value equals
  this exe's command, `Disabled` otherwise (including a stale path), `Unavailable` if the registry throws or the path is
  unknown. `SetEnabled` does not throw.

## W5 tracking controller (milestone W5, issues #16 #17, camera fallback of #24)

`Sway.Core.Controller` is the Mac `AppModel` tracking brain with the UI taken out: the pose to engine to focus loop, the
holds, the camera lifecycle and fallback, Focus Lock, the calibration library, and the status the tray shows. It is
portable (no Windows API, no UI framework) and is the only thing the tray, settings and calibration UIs talk to for
tracking. The Swift source is `SwayCore/Sources/SwayUI/AppModel*.swift`; the file split mirrors it
(`TrackingController.Tracking/Holds/FocusLock/PoseDemand/Library/Camera/Fallback/Grace/Lifecycle.cs`).

### Public types

- `TrackingController` (sealed, partial). Constructed from a `TrackingEnvironment`. `Dispose()` unhooks the platform
  events. `ShutdownAsync()` is "quit": it stops the camera and leaves the saved choice alone.
- `TrackingEnvironment` (record). Required: `PoseSource`, `Focus`, `Displays`, `Cameras`, `Session`, `Settings`,
  `HotKeys`, `MainThread`, `Scheduler`, `Clock` (monotonic seconds). Optional: `Now`, `Engine` (`ISwitchEngineMaking`,
  default `PoseSwitchEngine`), `FocusPollInterval` (default 1 s, null disables the repeating poll), `IsMainDisplayAsleep`,
  `Log` (never receives a window title).
- `IMainThread.Post(Action)`: queues work on the thread that owns the controller. It must queue (never run inline on the
  calling thread when that is not the main thread).
- `CameraStartException(CameraError)`: what `IPoseSource.StartAsync` throws for a start that fails. Any other exception
  becomes `ConfigurationFailed`.
- Value types: `TrackingStatus` (kind, text, reason; `Line`, `Tone`, `Icon`), `TrackingStatusKind`, `StatusTone`,
  `TrayIconState` (Tracking, Paused, FocusLock, Attention), `TrackingStatusSnapshot`, `CaptureText`, `TrackingHold`
  (via `TrackingHolds`), `TrackingLoop`, `FaceDebounce`, `SwitchLogEntry`, `CameraPermissionPrompt`, `SettingsKeys`.

State is read-only properties plus one `event Action StateChanged`. Properties: `TrackingEnabled`, `Status`,
`TrackingStatus`, `StatusLine`, `TrayIcon`, `TrackingHold`, `HasFace`, `IsCameraActive`, `Devices`, `Authorization`,
`PermissionPrompt`, `SelectedCameraId`, `PredictedCameraName`, `RunningDevice`, `FallbackCamera`, `LostCameraId`,
`DisconnectedCameraName`, `Layout`, `Library`, `ActiveCalibration`, `Sensitivity`, `LastDecision`, `TrackingFocus`,
`IsSwitching`, `SwayInFront`, `FocusLocked`, `FocusLockShortcut`, `ShortcutMessage`, `MenuShortcutMessage`,
`CalibrationRunning`, `SwitchLog`, `LogLines`, `WantedPoseDemand`. Two more events: `FocusSwitched(DisplayId)` and
`FocusLockToggledByShortcut(bool)` (for the on-screen notice).

Commands: `SetTrackingEnabledAsync`, `SelectCameraAsync`, `RefreshEnvironment`, `SetFocusLock`, `ToggleFocusLock`,
`SetFocusLockShortcut`, `ResetFocusLockShortcut`, `SetSensitivity`, `SetHoldTime`, `SetCooldown`, `SetSwitchConfidence`,
`UseInstantTiming`, `RestoreSensitivityDefaults`, `SaveToLibrary`, `RemoveFromLibrary`, `SetCalibrationRunningAsync`,
`SetPoseDebugVisible`, `SetFocusTestVisible`, `SetSwayInFront`, `ActiveDesktopChanged`, `ForegroundChanged`,
`TrackingTick` (the poll; the controller's own timer calls it).

### Environment and settings

Settings keys are the Mac ones: `sway.trackingEnabled` ("true"/"false"; written only by the user's own switch),
`sway.cameraDeviceID`, `sway.calibrationLibrary`, `sway.switchSensitivity`, `sway.focusLockShortcut` (absent means the
default `HotKeyShortcut.FocusLockDefault`, an empty string means no shortcut). JSON uses `EngineJson.Options`. Turning
Tracking off for a camera failure, a stream that ends, a lost camera or quit changes memory only, never the saved choice
(D23).

### Threading

- Create the controller on the main thread and call every command from it. `IMainThread` is the way back: the controller
  posts platform callbacks (`DevicesChanged`, `LayoutChanged`, `Occurred`, hotkeys), pose stream events, scheduler
  callbacks and focus completions to it, and `StateChanged` is raised there, after the work, only when something the UI
  shows changed (it never fires at the frame rate).
- The main thread must have a `SynchronizationContext` (the WinUI dispatcher has). The controller captures it for its
  awaits; it never uses `ConfigureAwait(false)` on a continuation that touches state. Focus requests and the poll are the
  one exception: they await off-thread, then `Post` the result.
- Camera work (start, stop, restart, retry, resume) runs one operation at a time in a chain, as the Mac does; nested work
  queues behind the running operation. Stale results are dropped by a stream generation, a loop epoch and cancellation.
- The Core `IDelayScheduler` runs all waits (holds, grace, device-list debounce, camera wait, retries, focus poll).
  Tests use `ManualDelayScheduler` and a fake `IMainThread`; nothing sleeps.

### Behavior notes

- Loop: a sample goes to the decider only while the camera is running and a calibration fits. Holds (priority: Focus
  Lock, Sway in front, displays changing, desktop changing, switching) lock the decider and refuse requests. "No face" is
  steadied by `FaceDebounce` (0.5 s); the decider sees the raw sample.
- Camera: `SuspendTracker` decides lock, sleep, display-off and session switches from `ISessionEvents`, seeded at launch
  from `IsSessionInactiveNow()`. Display changes that leave no calibration give the camera a 2 s grace. A chosen camera
  that vanishes for 5 s falls back to a calibrated one (built-in first); with none, Tracking turns off in memory with
  status "<camera> disconnected" and never resumes by itself. Retry waits use `RetryBackoff`.
- Pose demand: `PoseDemandPolicy` over Focus Lock, the Pose Debug window, the Focus Test window and calibration runs.

### Deliberate differences from the Mac

1. No Accessibility: there is no `needsAccessibility` status, no `notTrusted` result, no trust poll, and no permission
   explanation flow or `pendingIntent`. `NotDetermined` camera permission is simply tried; a source that is blocked
   reports `NotAuthorized` and the controller shows `PermissionPrompt = Denied`.
2. Wording: "Windows Settings", "virtual desktop" (the Mac "Space"; `ActiveDesktopChanged`, `TrackingHold.SpaceChanging`
   keeps the Mac enum name), "tray" (the Mac "menu bar"). `TrayIconState` replaces the SF Symbol glyph names.
3. Suspend is done here. The Mac capture layer suspends itself; the Windows `IPoseSource` does not, so the controller maps
   `SessionEvent` to Core `SuspendEvent`, tears the source down on the first reason, and rebuilds it when all clear.
4. A camera that vanishes reports `Failed(DeviceUnavailable)` (section 3). While Tracking was running, that is treated as
   an interruption (status "Interrupted", retried with `RetryBackoff` 1, 2, 5, then 10 s, a device-list change retries at
   once), not as the end of Tracking. A start that fails the same way keeps Tracking on only for the fallback's own
   restarts, as on the Mac.
5. The display layout is applied at once on `LayoutChanged`. The Mac waits for a notification plus a read; here the
   catalog hands the layout over. The display-change hold, its 0.75 s timeout and 3 s cap are unchanged.
6. Focus polling uses the scheduler (`FocusPollInterval`) and `ForegroundChanged()` replaces "app activation".
   "Sway in front" is set by the UI (`SetSwayInFront`); the controller has no window list.
7. Setup, the Gate test and the Focus Test are replaced by `SetCalibrationRunningAsync` (the camera runs at the full pose
   rate, switching is held, the camera is not swapped). `SetFocusTestVisible` only changes the pose demand; the Focus
   Test hold is not modeled. Fitting a profile from a trace is the calibration UI's job; it hands the result to
   `SaveToLibrary`.
8. `IsCameraActive` follows the Core `SuspendTracker` (capture requested), so it is false after a failed start.
9. A camera operation begins at once when nothing is ahead of it (no main-actor hop). One consequence: three rapid
   `SetTrackingEnabledAsync` calls start, stop and start again rather than coalescing to one start.
10. Not modeled: pose history, plots, the trace recorder, text readout throttling, `LookSummary` and the menu banner
    (presentation; the UI derives them from the state above).

### Interface gaps found

- `IPoseSource` has no way to carry a `CameraError` out of `StartAsync` except an exception; `CameraStartException` was
  added in `Sway.Core.Controller` for that. Adapters must throw it.
- `ISessionEvents` has no "locked at launch" signal separate from `IsSessionInactiveNow()`; a launch while the session
  is inactive starts suspended with reason `SessionInactive`.
- `ICameraCatalog` has no permission request; `Authorization()` is read, and `NotAuthorized` from the source is trusted.

## W5 tray app (issues #17, #18, #13: composition)

`src/Sway.App` is the composition root and the tray UI. Folders: `Hosting/` (log, dispatcher, system facts, Win32
shell calls), `Presentation/` (pure: icon appearance and palette, menu model, diagnostics snapshot and text, in-front
tracker), `Tray/` (GDI+ icon renderer, `TrayHost` with the `NotifyIcon`), `Windows/` (Diagnostics and a calibration
placeholder).

- **Composition** (`App.Compose`): `JsonFileSettingsStore` at `%AppData%\Sway\settings.json`, `WindowsCameraCatalog`,
  `WindowsPoseSource`, `WindowsDisplayCatalog`, `WindowsFocusController`, `WindowsHotKeyRegistrar`,
  `WindowsSessionEvents`, `RegistryLoginItem`, `DispatcherMainThread` (`BeginInvoke`, always queues), `TimerDelayScheduler`,
  a `Stopwatch` clock, `Log` into `RotatingFileLog`. The dispatcher's `SynchronizationContext` is installed before the
  controller is created. Single instance by named mutex `Local\Sway.TrayApp.SingleInstance`; a second launch exits.
  Tracking is restored by the controller from `sway.trackingEnabled`.
- **Shutdown**: Quit and `SessionEnding` both `await controller.ShutdownAsync()` (5 s cap; session end pumps a nested
  dispatcher frame), then adapters are disposed in `OnExit`.
- **Logging**: `%LocalAppData%\Sway\Logs\sway.log`, 1 MB, one rotation (`sway.log.1`). Dispatcher, AppDomain and
  unobserved task exceptions are logged; UI-thread exceptions are marked handled so the tray stays up (except OOM and
  similar). Only controller log lines (no titles) and exceptions are written.
- **Who calls the controller**: tray menu open/close and the active Sway window feed `SetSwayInFront` through
  `InFrontTracker` (any source true means in front). `ForegroundChanged()` comes from a `SetWinEventHook`
  (`EVENT_SYSTEM_FOREGROUND`, out of context, reports only) on the UI thread; `TrackingTick` is the controller's own
  1 s timer. `ActiveDesktopChanged()` is NOT wired: no cheap trigger exists (the foreground hook does not tell a desktop
  switch from a window switch). Wire it if a trigger is found.
- **Tray**: icon redrawn on `StateChanged` and on theme change (`SystemUsesLightTheme`); sizes 16/20/24/32, nearest to
  `SmallIconSize`. Amber (#E0902B) is the one non-brand color, for the Needs-you dot. Left click opens the menu through
  `NotifyIcon.ShowContextMenu` by reflection (no cursor API). The menu is rebuilt from `TrayMenuModel` on each open.
- **Diagnostics**: `DiagnosticsSnapshotBuilder` copies controller state; `DiagnosticsText.Build` makes the block shown
  and copied. A `LIVE-POSE-PLACEHOLDER` comment marks where yaw and pitch go. Monitor scale uses `GetDpiForMonitor`.
- **Orchestrator next**: (1) add `ProjectReference` to `src/Sway.App` in `Sway.Platform.Windows.Tests.csproj` and rename
  `tests/Sway.Platform.Windows.Tests/App/TrayPresentationTests.cs.pending` to `.cs` (it compiled and built clean with that
  reference); (2) replace `App.OpenCalibration()` body with the real calibration window and call
  `controller.SetCalibrationRunningAsync`/`SaveToLibrary`; (3) consider `FocusSwitched` for the edge glow and
  `FocusLockToggledByShortcut` for an on-screen notice (neither is subscribed).
