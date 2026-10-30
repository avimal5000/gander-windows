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
