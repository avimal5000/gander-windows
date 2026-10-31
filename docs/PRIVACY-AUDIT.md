# Privacy audit (Windows)

Sway's privacy promise: the camera stays on this PC, Sway reads only which way the head is turned, and video is never
recorded, saved, or sent anywhere. Nothing is typed, clicked, or moved on your behalf, and the cursor never moves.

This page says how each part of the promise is checked, and how to re-check it. Re-run the checks before every
release: `scripts/privacy-guard.sh` (source), `scripts/audit-privacy.sh <published folder>` (the shipped files), and the
runtime and hardware checks at the end.

Last static run: 2026-10-30 on a Release publish (`dotnet publish -r win-x64`) made on macOS, without the face model
file in the folder (so the model-hash check was exercised only against a deliberately wrong file). The runtime and
hardware checks below have not been run by a person yet.

## The promise, claim by claim

| Claim | How it is enforced | Evidence | 
|---|---|---|
| No network access | No networking API is called in shipped code. The app manifest asks for `asInvoker` and has no internet capability (Sway is an unpackaged desktop app, so Windows gives it no capability list; the proof is in the code and the files). Sway's own assemblies reference no `System.Net.*` assembly and import no networking DLL | `scripts/privacy-guard.sh` bans `HttpClient`, `System.Net.Sockets`, `WebSocket`, `WinHttp`, `WinINet`, `Windows.Networking`, `WebView2` and more in `src/` (runs in CI). `scripts/audit-privacy.sh` scans each `Sway*.dll`/`Sway*.exe` for networking assembly references, networking type names, networking DLL imports and embedded URLs; and checks the manifests |
| No video or images are saved | No API that encodes or writes pixels is called: the guard bans the bitmap encoders, `Bitmap.Save`, `MediaEncodingProfile`, `LowLagMediaRecording`, `LowLagPhotoCapture`, `IMFSinkWriter` and similar. A frame is converted for one inference and released | The guard; at runtime the check below lists files Sway holds open for writing |
| What is saved holds numbers only | Settings and the calibration library are JSON in `%AppData%\Sway\settings.json`: head angles, the display layout (names, sizes, positions) and the camera name. The log is `%LocalAppData%\Sway\Logs\sway.log` (1 MB, one rotation): status text only | `HeadPose` is yaw, pitch, roll with a timestamp; logs name the app and display, never a window title (`TrackingController.SwitchLogText`) |
| The camera runs only while it is needed | On only while Tracking is on and a calibration fits, or while Setup's camera check or calibration, Pose Debug, or the Focus Test needs it. Paused is off. Lock, sleep and display-off turn it off, and wake turns it back on only if it was wanted | `SuspendTracker` and about 150 controller tests (for example the camera tests in `tests/Sway.Core.Tests/Controller/CameraTests.cs`); the hardware check is below |
| No input is read or faked | No keyboard or mouse hook, no raw input, no `SendInput`, no cursor read or move. The Focus Lock shortcut uses `RegisterHotKey`, which delivers only that one combination | The guard bans `SetWindowsHookEx`, `SendInput`, `RegisterRawInputDevices`, `GetAsyncKeyState`, `GetCursorPos`, `SetCursorPos` and similar. Focus moves with `AttachThreadInput` + `SetForegroundWindow` (docs/decisions/02-focus.md), which sends no input |
| Window titles are not read or stored | Windows are identified by handle, process id, class name and process name only | `WindowSnapshot` has no title field; no `GetWindowText` call exists |
| The model is the one we chose | One face model is bundled, never downloaded by Sway, and its SHA-256 is pinned | `scripts/fetch-pose-model.sh` pins the hash; `scripts/audit-privacy.sh` checks the published `models/face_landmark.onnx` against that pin and that it is the only `.onnx` file |
| The only native dependencies are expected | ONNX Runtime (head pose), the .NET runtime, WPF's native pieces, Windows API-set stubs | `scripts/audit-privacy.sh` step 4 lists every native `.dll`/`.exe` in the folder that is not on the allow-list and fails on it (extend with `--allow-native <glob>` after review) |

## ONNX Runtime telemetry and model integrity

- ONNX Runtime's official Windows build can write trace events (TraceLogging, ETW) that Windows may upload to
  Microsoft when the user allows optional diagnostic data (see the `Privacy.md` in the Microsoft.ML.OnnxRuntime
  package). That is not a network call from Sway, and the runtime socket check below cannot see it. Sway therefore
  calls `OrtEnv.Instance().DisableTelemetryEvents()` before it creates any session (`LandmarkModel`), and
  `scripts/privacy-guard.sh` fails if that call disappears. This is compiled but not yet checked on a Windows PC (for
  example by listing ETW providers while Sway runs).
- At startup Sway reads the model file once, checks its SHA-256 against `ModelIntegrity.PinnedSha256`, and builds the
  session from those same bytes. A mismatch means the camera and tracking are unavailable and the log says so. The
  privacy guard fails if this pin and the one in `scripts/fetch-pose-model.sh` differ.

## What the static audit cannot see

- A self-contained publish contains the .NET runtime's own `System.Net.*.dll` files. They sit in the folder, but Sway's
  assemblies do not reference them; the audit prints a note, not a failure. ONNX Runtime is native code that the
  audit cannot read inside: it is a Microsoft-signed library used for inference only, and the runtime check below is
  what shows it opens no socket.
- A string-level scan finds names, not behavior. It is a tripwire for a mistake or a careless change, backed by the
  source guard and review, not a proof against deliberate obfuscation.

## How to re-run

```bash
# 1. Source guard (also run by CI)
scripts/privacy-guard.sh

# 2. Fetch the model, publish, then audit the output (Git Bash on Windows, macOS or Linux)
scripts/fetch-pose-model.sh
dotnet publish src/Sway.App -c Release -r win-x64 -o publish/Sway-win-x64
scripts/audit-privacy.sh publish/Sway-win-x64
```

Runtime sockets, with Sway running (PowerShell). Toggle Tracking, open Setup and quit while you watch; both lists must
stay empty:

```powershell
$p = (Get-Process Sway).Id
Get-NetTCPConnection -OwningProcess $p -ErrorAction SilentlyContinue
Get-NetUDPEndpoint   -OwningProcess $p -ErrorAction SilentlyContinue
```

Files held open for writing: Sysinternals Process Monitor (filter `Process Name is Sway.exe`, operation
`WriteFile`/`CreateFile`) should show only `%AppData%\Sway`, `%LocalAppData%\Sway\Logs`, and Windows' own caches; no
`.jpg`, `.png`, `.bmp`, `.mp4` or similar.

Long-running behavior (CPU, memory, handles, restarts) is `scripts\soak.ps1`, not part of this audit.

## Checks only a person can make

1. **Camera light and tray icon:** turn Tracking off in the tray menu: the webcam's light goes out within a second and
   Windows' "camera in use" tray icon disappears. Turn it on: both return. Quit Sway: both are gone.
2. **Lock and sleep:** with Tracking on, lock the PC (Win+L): the light goes out. Unlock: it returns. Sleep and wake
   the same way; with Tracking off, wake must not turn the camera on.
3. **Settings, Privacy and security, Camera:** Sway (or "Sway.exe", under desktop apps) is listed with a recent
   "last used"; nothing else was added by Sway. No Microphone, Location or Screen capture entries from Sway.
4. **Unplug the webcam** with Tracking on: the tray says the camera is disconnected, the light is gone, and replugging
   resumes tracking without restarting Sway.
5. **A firewall prompt must never appear.** Run Sway with Windows Firewall in "notify me" mode, or with a tool such as
   GlassWire or TCPView, for a few minutes of normal use: no connection attempt, no firewall dialog.
6. **SmartScreen:** an unsigned test build shows "More info, Run anyway" once. That is expected until the build is
   signed; it says nothing about network use.
