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
