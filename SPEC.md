# Sway for Windows: Spec (current phase: W0, foundations)

Derived from PLAN.md, which stays the source of truth for scope. The Mac app's `SPEC.md` and `CONTRACT.md`
(`avimal5000/sway`) define the behavior; this file lists only what is decided for Windows. CONTRACT.md defines the
code-level interfaces.

## 1. Product

Sway is a quiet Windows system tray utility. It switches keyboard focus to the monitor you are looking at, using head
pose from a webcam, entirely on-device. It works per monitor: look at another monitor, dwell briefly, and typing goes
to the last-used window there. The cursor never moves.

## 2. Decisions and assumptions

| # | Decision | Source |
|---|---|---|
| D1 | Behavior matches the Mac app (per-display focus, head pose only, last-used window, cursor never moved, uncertain = no switch). Differences are numbered decisions here | PLAN.md |
| D2 | .NET 8, C#, WPF shell, WinForms `NotifyIcon` for the tray. `Sway.Core` is portable and references no Windows API | PLAN.md |
| D3 | Work happens on the Mac; every PR builds on a Windows runner and publishes `Sway-win-x64` and `SwayTools-win-x64` for the user to try on the Windows desktop | User, 2026-10-30 |
| D4 | One third-party dependency is allowed for head pose: ONNX Runtime plus a bundled face model. Audited for network use; the model is never downloaded | User, 2026-10-30 |
| D5 | Unsigned test programs and builds may be run on the user's desktop (SmartScreen: More info, Run anyway) | User, 2026-10-30 |
| D6 | Target Windows 10 version 2004 (build 19041) and later, x64. Windows 11 is the main target | Assumption |
| D7 | Focus Lock default shortcut `Ctrl+Alt+Shift+L` (Windows reserves Win-key combinations). Configurable | Assumption |
| D8 | Settings are JSON under `%AppData%\Sway`; logs under `%LocalAppData%\Sway\Logs`; per-user `Run` key for "Open Sway at login" (off by default) | Assumption |
| D9 | The synthetic Alt key workaround for foreground focus is **not used** unless spike S2 shows it is the only reliable way and the user approves, with the privacy wording changed first | Open (PLAN.md section 9, item 6) |

## 3. The test setup (the user's desktop)

| Item | Value |
|---|---|
| Webcam | Logitech C922 (the one used with the Mac app) |
| Monitors | LG UltraGear on the left, BenQ on the right. Sizes, resolutions, and scaling to be read by the spike tools |
| Windows version | To be read by the spike tools (the user has not checked it yet) |

## 4. Manual checks the user must do (cannot be automated here)

Filled in per milestone, in the style of the Mac app's section 9.
