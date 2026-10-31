# 02: Moving keyboard focus on Windows (spike S2)

**Question.** Can a background program give another window the keyboard focus, without moving the cursor and without
synthesizing input?

**Run.** 2026-10-30 on the user's desktop (Windows 11 25H2, build 26200, x64). `GanderTools focus`: for each attempt the
user clicked a window on one monitor, so the foreground belonged to another app, then the program tried to focus the
topmost window on the other monitor. Two rounds in each direction. Targets were Chrome and Discord (both Chromium).

| Method | Focused | Cursor moved |
|---|---|---|
| `SetForegroundWindow` alone | 0 of 4 | 0 |
| `AttachThreadInput` to the foreground thread, `BringWindowToTop`, `SetForegroundWindow`, detach | 4 of 4 | 0 |

**Result.** Plain `SetForegroundWindow` is refused by Windows' foreground lock when the caller is not the foreground
process, as expected. Attaching to the foreground thread's input queue first works every time, in both directions, and
the cursor never moves. No synthesized key press is needed for these windows.

**Decision.** Use the attach recipe (SPEC D9: the Alt-key workaround is not needed and stays unused). Always detach in a
`finally`. Verify the result by reading the foreground window afterwards, and treat a failure as "no switch".

**Not yet covered** (W7 hardening checks): UWP windows (Settings, Calculator), a full-screen video or game, an
elevated (administrator) window, a hung foreground app, and the same recipe while Gander runs as the tray app instead of
from a terminal. The "keyboard focus confirmed" column in the program's output reads the target thread's focus window and
is not conclusive on its own; the foreground check is.
