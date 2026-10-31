# Status

Last updated: 2026-10-31

| Milestone | State |
|---|---|
| W0 Foundations | Done (PR 29) |
| W1 Spikes (go/no-go) | S1, S2, S3 merged. Head pose separates the user's monitors clearly; the face re-find fix (PR 36) awaits the user's 3d test on the camera |
| W2 Core engine port | Runtime pieces merged (PR 37). Engine (calibration, classifier, switch decider) with 320 tests awaits review |
| W3 to W8 | Not started |

Next: merge the engine, then W3 (camera and live pose behind the engine) and W4 (focus and displays). The user tries GanderTools 3d.

W7 hardening (branch feat/w7-hardening): focus verification needs the window in front on two reads and a display that
already has the front window is left alone; a repeat request for the display Gander just focused waits 2, 4, 8, 10 s and
the log says when focus was taken back; virtual desktop switches, the Focus Lock shortcut and switches now reach the
controller and tray; the display hold re-reads the layout when it times out; camera status changes are logged.
New: `scripts/audit-privacy.sh <publish folder>`, `scripts/soak.ps1`, `docs/PRIVACY-AUDIT.md`. Only the user can check on
the PC: virtual desktop switching, the unplug and replug of the webcam, hot-plugging a monitor, and a soak run.

## 2026-10-31 update

- W0 to W5 done and confirmed on the user's PC (calibration, tracking, switching to both monitors).
- W6: setup window, Look-here overlay, Settings window and themed tray menu merged (visuals not yet checked on the PC). Icon artwork and edge glow still to do.
- W7: focus verify and repeat guard, virtual desktop watcher, privacy audit and soak scripts, audit fixes merged. Runtime checks on the PC (hot-plug, camera unplug, soak) still to do.
- W8: release workflow on v* tags works; `v0.1.0-rc2` is a pre-release with a zip and checksum. Unsigned (SmartScreen warning). Code signing (issue 28) waits on the owner.
- Docs: PRIVACY.md and TERMS.md drafts (placeholders: contact email, governing law; no LICENSE file yet), PRIVACY-AUDIT.md, AUDIT-2026-10.md.
- Open: face model license verification, large refactors and camera timeouts from the audit (F03, F04, F23 to F34), NuGet lock file and vulnerability scan.
- Debug windows (branch feat/debug-windows): the Mac's Pose Debug, Head-Pose Gate Test and Focus Test windows, reachable from the tray menu after Settings. Logic is pure and tested (`PoseDebugModel`, `GateTestModel`, `FocusTestModel`, `CameraDemand`); the WPF windows are compiled only. The owner should open each on the PC (camera on, two monitors): Pose Debug live numbers, plots and bands, Record and Stop and save (file under `%LocalAppData%\Gander\Traces`); Gate Test marker on each monitor and the Copy button; Focus Test buttons raise the right window and the pointer does not move.

## 2026-10-31 rebrand: Sway is now Gander

- The product is called Gander and the mark is a goose (same forest, sage and ivory colours). New app icon, tray glyph (solid, hollow outline when paused, lock badge, alert dot) and installer; `scripts/make-icon.swift` redraws the icon.
- The program is `Gander.exe`; releases are `Gander-<version>-win-x64.zip` and `...-setup.exe`. Settings are in `%AppData%\Gander`, logs in `%LocalAppData%\Gander\Logs\gander.log`.
- An install of the earlier name keeps working: `EarlierNameMigration` copies `%AppData%\Sway\settings.json` if there is none under the new name and moves the "start with Windows" entry; the installer removes the old `Sway.exe` and shortcuts, and the uninstaller also offers to delete the old data folders.
- Code names follow: solution `Gander.sln`, projects and namespaces `Gander.*`, `GanderTools.exe`, `installer/Gander.iss`. Only the migration and the installer's clean-up keep the old name `Sway`, on purpose.
- Name risk: a 2012 US trademark filing for GANDER (software, owner Great Heights Inc.) turned up in a web search. Its status is not checked; the owner should run a USPTO search before any public launch.
