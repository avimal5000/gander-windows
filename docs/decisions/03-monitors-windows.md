# 03: Monitors and windows (spike S3)

**Run.** 2026-10-30 on the user's desktop. `GanderTools info`.

- **System.** Windows 11 25H2 (build 26200; the registry product name still says "Windows 10"), x64, 16 logical
  processors, Logitech C922 found, camera access allowed for the user, the machine, and desktop apps.
- **Monitors.** Two, both at 100% scale: 1920x1080 at (-1920, 0) on the left, and 2560x1440 at (0, 0), primary, on the
  right. Monitor order by left edge matches the physical order. Mixed-scale monitors are untested on this setup.
- **Windows.** `EnumWindows` returns topmost first, so the first eligible window whose nearest monitor matches is the
  last-used window on that monitor. The filter (visible, not minimized, not cloaked, not a tool window or owned popup,
  not shell chrome, on the current virtual desktop) left a sensible list: 3 windows across both monitors, with 13
  cloaked, 273 invisible and 6 tool windows excluded.
- **Monitor of a window.** `MonitorFromWindow(..., NEAREST)` picks by largest overlap, so a window straddling two
  monitors belongs to the one it covers more.

**Decision.** The W4 window tracker uses this enumeration and filter. Cloaked windows (other virtual desktops, hidden
UWP) are excluded by `DwmGetWindowAttribute`; `IVirtualDesktopManager` is the second check.
