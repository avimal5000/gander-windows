# Gander for Windows: Spec (current phase: W0, foundations)

Derived from PLAN.md, which stays the source of truth for scope. The Mac app's `SPEC.md` and `CONTRACT.md`
(`avimal5000/sway`) define the behavior; this file lists only what is decided for Windows. CONTRACT.md defines the
code-level interfaces.

## 1. Product

Gander is a quiet Windows system tray utility. It switches keyboard focus to the monitor you are looking at, using head
pose from a webcam, entirely on-device. It works per monitor: look at another monitor, dwell briefly, and typing goes
to the last-used window there. The cursor never moves.

## 2. Decisions and assumptions

| # | Decision | Source |
|---|---|---|
| D1 | Behavior matches the Mac app (per-display focus, head pose only, last-used window, cursor never moved, uncertain = no switch). Differences are numbered decisions here | PLAN.md |
| D2 | .NET 8, C#, WPF shell, WinForms `NotifyIcon` for the tray. `Sway.Core` is portable and references no Windows API | PLAN.md |
| D3 | Work happens on the Mac; every PR builds on a Windows runner and publishes `Gander-win-x64` and `SwayTools-win-x64` for the user to try on the Windows desktop | User, 2026-10-30 |
| D4 | One third-party dependency is allowed for head pose: ONNX Runtime plus a bundled face model. Audited for network use; the model is never downloaded | User, 2026-10-30 |
| D5 | Unsigned test programs and builds may be run on the user's desktop (SmartScreen: More info, Run anyway) | User, 2026-10-30 |
| D6 | Target Windows 10 version 2004 (build 19041) and later, x64. Windows 11 is the main target | Assumption |
| D7 | Focus Lock default shortcut `Ctrl+Alt+Shift+L` (Windows reserves Win-key combinations). Configurable | Assumption |
| D8 | Settings are JSON under `%AppData%\Gander`; logs under `%LocalAppData%\Gander\Logs`; per-user `Run` key for "Open Gander at login" (off by default) | Assumption |
| D9 | Foreground focus uses `AttachThreadInput` to the foreground thread, then `BringWindowToTop` and `SetForegroundWindow`, verified afterward. Spike S2 showed 4 of 4 successes with it (0 of 4 without), and the cursor never moved. The synthetic Alt key workaround is **not used**, so the "no synthesized input" promise stands | Spike S2, docs/decisions/02-focus.md |

## 3. The test setup (the user's desktop)

| Item | Value |
|---|---|
| Webcam | Logitech C922 (the one used with the Mac app) |
| Monitors | Left: 1920x1080 at 100% scale. Right (primary): 2560x1440 at 100% scale. Read by spike S3. The user believes the LG is on the left; the sizes suggest the 1440p LG is on the right |
| Windows version | Windows 11 25H2, build 26200, x64, 16 logical processors |

## 4. Manual checks the user must do (cannot be automated here)

Filled in per milestone, in the style of the Mac app's section 9.
