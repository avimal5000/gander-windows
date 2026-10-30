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
