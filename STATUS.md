# Status

Last updated: 2026-10-30

| Milestone | State |
|---|---|
| W0 Foundations | Done (PR 29) |
| W1 Spikes (go/no-go) | S1, S2, S3 merged. Head pose separates the user's monitors clearly; the face re-find fix (PR 36) awaits the user's 3d test on the camera |
| W2 Core engine port | Runtime pieces merged (PR 37). Engine (calibration, classifier, switch decider) with 320 tests awaits review |
| W3 to W8 | Not started |

Next: merge the engine, then W3 (camera and live pose behind the engine) and W4 (focus and displays). The user tries SwayTools 3d.

W7 hardening (branch feat/w7-hardening): focus verification needs the window in front on two reads and a display that
already has the front window is left alone; a repeat request for the display Sway just focused waits 2, 4, 8, 10 s and
the log says when focus was taken back; virtual desktop switches, the Focus Lock shortcut and switches now reach the
controller and tray; the display hold re-reads the layout when it times out; camera status changes are logged.
New: `scripts/audit-privacy.sh <publish folder>`, `scripts/soak.ps1`, `docs/PRIVACY-AUDIT.md`. Only the user can check on
the PC: virtual desktop switching, the unplug and replug of the webcam, hot-plugging a monitor, and a soak run.
