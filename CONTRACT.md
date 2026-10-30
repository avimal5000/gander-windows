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
