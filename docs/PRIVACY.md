# Sway Privacy Policy

**Applies to:** Sway for macOS and Sway for Windows.
**Effective date:** 2026-10-30
**Operator:** Avi Malhotra, an individual developer (GitHub: [avimal5000](https://github.com/avimal5000)).
**Contact:** [add contact email before publishing]

> This is a plain-language draft, not legal advice. It should be reviewed by a lawyer before commercial use.

## The short version

- Sway does not collect your data. I (the developer) never receive anything from your computer.
- Sway has no network code. It does not phone home, has no telemetry, no crash reporting, no ads, no accounts, and no auto-update.
- Sway uses your webcam to see which way your head is turned. The video is processed on your computer, in memory. It is not saved and not sent anywhere.
- Sway keeps a few settings and a small log on your computer. You can delete them at any time (see "Delete your data" below).

## What Sway does with your camera

Sway reads frames from your webcam to estimate your head direction (left, right, up, down). It uses that to decide which monitor you are looking at and moves keyboard focus to the last window you used on that monitor.

- Frames are handled in memory and then dropped. Sway does not write pictures or video to disk.
- The camera is on only when Sway needs it: while Tracking is on, or while you use Setup, the camera check, calibration, Pose Debug, or the Focus Test. When you pause Tracking, the camera turns off. Sleep, screen lock, and display sleep also turn it off.
- Your camera's normal indicator (the light, and on Windows the "camera in use" icon) shows when the camera is on.
- Sway does not use the microphone.

## Who processes the video

- **macOS:** head direction is worked out with Apple's Vision framework, which is part of macOS and runs on your Mac. Apple's own handling of your system is covered by Apple's terms, not this policy.
- **Windows:** head direction is worked out with ONNX Runtime (from Microsoft, MIT license) and a face-landmark model (derived from Google MediaPipe Face Landmarker, converted to ONNX). Both are bundled with the download and run on your PC. Sway does not download the model. See `THIRD-PARTY-NOTICES.md` in the Windows download for their licenses.

## What Sway stores on your computer

Everything below stays on your computer. None of it is sent to me or anyone else by Sway.

**On both systems**

- Your settings and choices (for example, sensitivity, accent color, the Focus Lock shortcut, whether to start at login).
- Your calibration: the head angles you recorded while looking at each monitor, and a description of your monitor layout (monitor names, sizes, and positions), plus the name of the camera you used. Calibration holds numbers, not pictures.

**macOS**

- Settings and calibration are in the app's preferences (`UserDefaults`), under the bundle ID `com.avimal5000.sway`.
- Sway writes status messages to the macOS system log (not to its own file). These name the app and the monitor involved in a switch. They never include window titles.
- In developer (Debug) builds only, you can record a pose trace in Pose Debug. A trace is a list of head angles with timestamps, saved in `~/Library/Application Support/Sway/Traces`. Normal downloads do not save traces.
- Pose Debug can copy a text summary of a pose trace (head angles, no images) to your clipboard. This happens only when you click the button.

**Windows**

- Settings and calibration are in `%AppData%\Sway\settings.json`.
- A log is in `%LocalAppData%\Sway\Logs\sway.log` (about 1 MB, with one older file, `sway.log.1`). It holds status text: start-up and shutdown, camera and display events, errors (which can include technical details such as file paths), and one line per switch that names the app and monitor (for example "Focused Notepad on Display 2"). It never holds window titles or images.
- If you turn on "Start with Windows", Sway adds one value named `Sway` under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.
- Diagnostics: the tray menu has "Diagnostics", with a "Copy diagnostics" button. It shows the Sway version, Windows version, system architecture, tracking state, camera and monitor names, calibration summary, recent switch lines (app names), and recent log lines. It contains no window titles and no images. Copying puts that text on your clipboard only when you press the button. Sway never sends it. If you paste it somewhere (for example into a GitHub issue), you are the one sharing it, so read it first.

## What Sway can see about your apps and monitors

To move focus, Sway needs to know which windows are on which monitor.

- **macOS:** it uses the Accessibility permission to raise and focus a window. Window titles are shown only inside the Focus Test window and are not logged or stored.
- **Windows:** it identifies windows by handle, process ID, class name, and process name. It does not read window titles.
- Sway does not read your keystrokes or mouse. It does not type, click, or move your cursor for you. Its Focus Lock shortcut uses the system's hotkey feature, which gives Sway only that one key combination.

App names, monitor names, and camera names can be personal. They stay on your computer, and appear in the log and in a diagnostics copy only as described above.

## Permissions

- **macOS:** Camera (to see head direction) and Accessibility (to focus windows). Sway asks for no other permissions. You can revoke either in System Settings, Privacy & Security.
- **Windows:** Camera. Windows asks the first time. You can revoke it in Settings, Privacy & security, Camera. Sway asks for no other permissions.

Without these permissions Sway cannot work.

## Network

Sway makes no network connections. It has no update checker, no analytics, and no crash reporting. To update, you download a new version yourself.

I check this in the project's own audit notes (`docs/PRIVACY-AUDIT.md`) with automated source checks and with scans of built files. Those checks are a safeguard, not a guarantee against every possible mistake. Windows itself, macOS itself, and other software on your computer may still use the network, and that is outside Sway.

## Downloads and GitHub

Sway is distributed through the GitHub Releases page of its repository ([macOS](https://github.com/avimal5000/sway), [Windows](https://github.com/avimal5000/sway-windows)). When you visit GitHub or download a file, GitHub processes your visit (for example your IP address) under GitHub's own privacy statement. I do not get that data from the app, and this policy does not cover GitHub's handling of it. Each release has a `.sha256` file so you can check the download.

The downloads may not be signed or notarized, so macOS or Windows SmartScreen may warn you when you first open them. That warning is about who signed the file. It is not about network use.

## Children

Sway is a general-purpose utility, not aimed at children. Because Sway collects nothing, it does not knowingly collect data from children. If a child uses a computer with Sway, a parent or guardian is responsible for that use. See also "Camera and other people" in the Terms.

## Your rights and your data

I hold no personal data about you, so there is nothing for me to access, correct, export, or delete. Your data is the local files listed above, and you control them.

**Delete your data**

*macOS:* quit Sway, then in Terminal run `defaults delete com.avimal5000.sway`, and delete `~/Library/Application Support/Sway` if it exists. Delete Sway from Applications. To remove permissions, open System Settings, Privacy & Security, and remove Sway from Camera and Accessibility. If you enabled start at login, turn it off first in Sway's settings or in System Settings, General, Login Items.

*Windows:* turn off "Start with Windows" in Sway's Settings if you turned it on, then quit Sway from the tray. Delete the Sway folder you unzipped. Delete `%AppData%\Sway` (settings and calibration) and `%LocalAppData%\Sway` (logs).

## Changes to this policy

If I change this policy, I will update the effective date at the top and publish the new text in the repository. Earlier versions stay available in the repository history. If a future version of Sway ever adds network access or collects data, this policy will change before that version is released.

## Contact

[add contact email before publishing]. You can also open an issue on the GitHub repository.
