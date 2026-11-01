# Gander for Windows: Build Plan

**Look. Focus. Flow.** A quiet Windows system tray utility that switches keyboard focus to the monitor you are looking at, using head pose from a webcam, entirely on-device.

Status: planning only. No code until the go-ahead. This follows the macOS plan (`avimal5000/gander`, `PLAN.md`) and reuses its product decisions, its behavior spec, and its working method. The code is a rewrite: nothing in Swift, SwiftUI, AppKit, Vision, or the Accessibility API exists on Windows.

## 1. Decisions locked in

| Topic | Decision |
|---|---|
| Behavior | Same as the Mac app: per-display focus, head pose only, last-used window on the target display, cursor never moved, silent feedback (tray icon state plus an optional thin edge glow), "uncertain = do nothing" |
| Source of truth for behavior | The Mac repo's `SPEC.md` and `CONTRACT.md`. Where Windows must differ, the difference is a numbered decision in this repo's `SPEC.md`, with its reason |
| Repo | `avimal5000/gander-windows`, private |
| Where the work happens | On the Mac (this orchestrator session). Windows is reached through GitHub: every PR builds on a Windows runner and publishes a downloadable build for the user to try on the Windows desktop |
| Distribution | Free download first, unsigned (SmartScreen shows a warning, as Gatekeeper does on the Mac). Code signing later |
| Windows versions | Windows 11 is the target; Windows 10 22H2 best effort. To confirm with the user's desktop (section 9) |
| CPU architecture | x64 first (runs on Windows on ARM through emulation). A native ARM64 build comes later if it matters |
| Out of scope (first release) | More than 3 displays, eye gaze, workspace profiles, Microsoft Store, auto-update |

## 2. Tech stack

The Mac app has zero third-party dependencies. Windows has no equivalent of Vision's head pose, so **one machine-learning dependency is unavoidable**. Everything else stays in the box.

- **.NET 8 (LTS) and C#**. Nullable reference types on, warnings as errors.
- **Shell: WPF** for settings, setup, and the overlay windows. Tray icon through the in-box WinForms `NotifyIcon` (no extra package). WinUI 3 was considered; WPF is simpler for a tray app with click-through topmost overlays and has no packaging requirement.
- **Camera:** Windows Media Capture (`Windows.Media.Capture`) with a frame reader, or Media Foundation. The spike picks one.
- **Head pose:** a bundled face-landmark model run locally with ONNX Runtime, then a pose solve (landmarks to yaw, pitch, roll). Candidate: MediaPipe Face Mesh converted to ONNX (Apache-2.0). Alternative: MediaPipe's own runtime through a native bridge. The model file ships inside the app and is never downloaded. Decided by spike S1.
- **Windows APIs (P/Invoke, no packages):** `EnumDisplayMonitors`, `GetMonitorInfo`, `EnumWindows` (z-order, topmost first), `DwmGetWindowAttribute` (cloaked windows), `IVirtualDesktopManager`, `SetForegroundWindow` and its workarounds, `RegisterHotKey` for Focus Lock, `WTSRegisterSessionNotification` and power notifications for lock and sleep.
- **Settings:** JSON under `%AppData%\Gander\`. Autostart through the per-user `Run` key, off by default. No other registry writes.
- **Logs:** `Microsoft.Extensions.Logging` is avoided; a small local file logger writes to `%LocalAppData%\Gander\Logs` with a size cap. Nothing leaves the machine.
- **Quality:** `dotnet format` (style gate), Roslyn analyzers with warnings as errors, xUnit tests, a privacy guard script.

## 3. Architecture

Mirrors the Mac modules so the contracts carry over. The portable half builds and tests on the Mac; the Windows half compiles on the Mac (`EnableWindowsTargeting`) and runs on Windows.

```
Gander.App          (WPF, net8.0-windows)  tray, settings, setup, glow overlay, notice
 ├─ Gander.Core            (net8.0, portable: builds and tests on macOS, Linux, and Windows)
 │    models, CalibrationModel, classifier, SwitchDecider state machine, SwitchSensitivity,
 │    AdaptiveFrameRate, SuspendTracker, RetryBackoff, TrackingHold priority, pose trace replay
 ├─ Gander.Platform.Windows (net8.0-windows)
 │    CameraService, PoseEstimator, DisplayTracker, WindowTracker, FocusController,
 │    SessionMonitor (lock, sleep, display sleep), HotKey
 └─ Gander.Tools            (spike and diagnostic console programs, run on the Windows desktop)
Gander.Core.Tests / Gander.Platform.Windows.Tests
```

Rule: `Gander.Core` never references a Windows API. Everything Windows-specific sits behind an interface that Core defines (`IPoseSource`, `IFocusPlatform`, `IDisplayCatalog`, `ISessionEvents`), so the engine is testable with fakes and replayed traces, exactly as on the Mac.

## 4. Parity strategy (how we know the port behaves like the Mac app)

1. **Golden vectors.** A script in the Mac repo runs the Swift engine over recorded and synthetic traces and writes input and expected-decision sequences as JSON. The C# engine must reproduce them exactly. This pins the dwell, margins, hysteresis, cooldown, and the Instant preset.
2. **Fixtures.** The Mac repo's numbers-only gate traces are copied in. They test the engine, not the pose estimator: Windows computes angles differently from Vision, so sign and zero conventions are fixed once in `CONTRACT.md` and a mapping layer is tested.
3. **Decisions carried over:** D18 (nearest-center classifier plus an "elsewhere" box), D19 (topmost normal window is the last-used one), D42-style verified focus (raise, verify, fall back), D46 (camera loss turns Tracking off), D49 (adjustable pause, Instant timing).

## 5. Privacy guarantees (enforced, not promised)

Same promises as the Mac app, same enforcement style:

- Frames are processed in memory and dropped. No pixel or video is written or sent. Only numeric pose traces may be saved.
- No network: no networking API in shipped code, no networking library linked. A guard script runs in CI and fails on `HttpClient`, `System.Net.Sockets`, `WebClient`, and similar. **The ML dependency is audited for network use** (ONNX Runtime has none). The model is bundled, never fetched.
- Camera on only while Tracking is on. Pause, lock, sleep, and quit release the camera (the webcam light goes out).
- No input is read or faked: no low-level keyboard or mouse hooks, no `SetWindowsHookEx`, no `SendInput`, and the cursor never moves. Focus Lock uses `RegisterHotKey`, which delivers only that one combination.
- **Open tension:** Windows often refuses to let a background app take the foreground. The common workarounds are attaching to the foreground thread's input queue, or sending a synthetic Alt key press. The first is acceptable. The second would break the "no synthesized input" promise. Spike S2 finds out whether `AttachThreadInput` plus the usual calls is enough. If not, the promise is reworded in `SPEC.md` as an explicit decision, never bent quietly.
- No accounts, analytics, or crash-reporting services.

## 6. Biggest risks (to retire early)

1. **Head pose without Vision.** Accuracy, latency, and CPU of a bundled landmark model plus a pose solve are unknown. Spike S1 measures separability on the user's real desktop and webcam. **Go/no-go gate.**
2. **Foreground lock.** `SetForegroundWindow` fails unless Windows considers the caller entitled. Also, windows of an elevated (administrator) app cannot be focused from a non-elevated one. Spike S2 maps what works across normal apps, UWP apps, full-screen apps, and elevated windows, and documents the fallbacks.
3. **Window and monitor model.** Virtual desktops, cloaked windows, always-on-top windows, minimized windows, and per-monitor DPI scaling (mixed 100% and 150% monitors) each break a naive "topmost window on this monitor". Spike S3 settles it.
4. **External webcam geometry.** A USB webcam clipped to one monitor sees different angles from a laptop camera. Calibration must cope, and setup copy must say where to put the camera.
5. **Testing hardware from the Mac.** Solved by the portable core, golden vectors, replayed traces, and CI on Windows. Real-camera and real-monitor checks stay a manual checklist for the user, with downloadable builds on every PR.
6. **SmartScreen and antivirus false positives** on an unsigned, camera-using tray app. Plan for the warning in install docs; signing later.
7. **Model licensing and size.** Confirm the license of the chosen model and keep the installer small.

## 7. Milestones (each becomes a GitHub milestone with issues)

| # | Milestone | Outcome / exit criteria |
|---|---|---|
| W0 | Foundations | Repo, solution skeleton, CI on `windows-latest` (build, test, format, privacy guard), PR template, issue templates, labels, milestones and issues seeded from this plan, PR builds publish a downloadable artifact |
| W1 | Spikes (go/no-go) | S1 pose pipeline, S2 focus switching, S3 monitors and windows, each a small console program the user downloads and runs on the Windows desktop. Results recorded in `docs/decisions/`. Gate: head pose separates the user's displays, and focus can be moved reliably |
| W2 | Core engine port | `Gander.Core`: models, calibration, classifier, decider, sensitivity, adaptive frame rate, suspend tracker, hold priority. All golden-vector tests pass. Runs on the Mac with no Windows involved |
| W3 | Camera and live pose | Camera capture and the chosen pose pipeline behind `IPoseSource`; debug window showing live yaw and pitch; trace recorder |
| W4 | Focus and displays | Monitor tracking, last-used window per monitor, verified focus switch from a debug button without moving the cursor, layout-change detection |
| W5 | End-to-end loop | Tray app: camera to pose to decider to focus. Pause and Resume, Focus Lock hotkey, status icon. First daily-driver build |
| W6 | Setup, calibration, settings, design | Guided setup (camera, layout, calibration), sensitivity and timing settings, the Mac palette (forest green, sage, ivory, charcoal), icon, edge glow, restrained motion, light and dark |
| W7 | Hardening | Lock, sleep, display sleep, hot-plug, camera loss and fallback, virtual desktops, full-screen apps, CPU and battery budget, DPI, privacy audit, soak test |
| W8 | Release | Installer or portable zip, release workflow on `v*` tags, install notes for SmartScreen, then signing when the user decides |

Order: W0, then W1 (gate). W2 can run alongside W1 because it is portable. Then W3 and W4 (overlap), W5, W6, W7, W8.

## 8. How we work

Same method as the Mac project:

- **Roles.** The orchestrator (this Mac session) plans, writes the specs, creates and owns the solution and project files and CI, reviews, and merges only with the user's approval. Builders (Sonnet agents) implement one module per PR in isolation. An Opus review reports findings, and the orchestrator fixes them.
- **Docs.** `SPEC.md` (numbered decisions D#, features F#), `CONTRACT.md` (interfaces and rules), `STATUS.md` (kept current), `docs/decisions/` (spike results).
- **Branches and PRs.** Branch per issue, PR template with a privacy and behavior checklist, squash merge with `--delete-branch` and a clean `--body`. Never put the CI-skip marker in any commit message or PR body: it silences CI, and a squash merge copies it into main.
- **CI on Windows runners.** On a private repo on the Free plan, Windows minutes bill at 2x. PR-only CI. Each PR publishes `Gander-win-x64.zip` as a workflow artifact.
- **Windows testing loop.** The user downloads the artifact from the PR's checks, runs it, and reports what happened. A "Copy diagnostics" button comes early (W3) so reports are easy: Gander version, Windows build, settings, cameras, monitors, status, recent log lines. Local only.
- **Hardware checks list** (user-run) is kept in `SPEC.md` like the Mac's section 9.

## 9. Needed from the user before W1

1. **Windows edition and build** on the desktop (Settings, System, About), and whether it is x64 or ARM.
2. **Webcam model** and where it sits (on which monitor, centered or off to one side).
3. **Monitor setup:** how many, sizes, resolutions and scale settings, and left-to-right order.
4. **Willingness to run unsigned test programs** from GitHub on the desktop (SmartScreen: More info, Run anyway), and to send back the text they print. Needed for the spikes.
5. **Consent to one third-party dependency** (ONNX Runtime plus a bundled face model), audited for network use.
6. **Policy on the Alt-key workaround** if S2 shows it is the only reliable way: allowed as a last resort with the privacy wording changed, or not allowed.

Defaults if not answered: private repo, name "Gander", Focus Lock default `Ctrl+Alt+Shift+L` (Win-key combinations are reserved by Windows), x64, Windows 11, unsigned first release.

## 10. Definition of done for the first release

- Look at another monitor, dwell the configured time, and typing goes to the last-used window there; the cursor and selections stay untouched.
- Ambiguous poses, glances, face lost, camera busy, or Focus Lock on: no switch.
- The webcam light is on only while Tracking is on. Verified: no video written, no network access.
- Setup in under 2 minutes including calibration; recalibration in under 30 seconds.
- CPU about 6% or less when idle with no face, matching the Mac.
