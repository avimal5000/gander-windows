# Installing Sway for Windows

Sway is a quiet tray program. It uses your webcam to see which way your head is turned and moves keyboard focus to the
last window you used on that monitor. Nothing leaves your PC: no network code, no saved video or pictures.

## Install

1. Unzip the whole `Sway-<version>-win-x64.zip` into a folder you keep, for example `C:\Users\<you>\Sway` or
   `C:\Program Files\Sway`. Keep the files together. Sway needs the `models` folder and the `.dll` files next to
   `Sway.exe`.
2. Run `Sway.exe`.
3. This build is **unsigned**, so Windows SmartScreen shows "Windows protected your PC" the first time. Choose
   **More info**, then **Run anyway**. If the file was marked as downloaded, you can also right-click the zip before
   unzipping, choose Properties, tick **Unblock**, then OK.
4. The "Set up Sway" window opens on first launch. Allow camera access when Windows asks (Settings, Privacy &
   security, Camera, "Let desktop apps access your camera" must be on).
5. Follow the steps: check your framing, then look at each monitor twice when the green "Look here" marker shows.
6. Choose **Start tracking**. Sway lives in the tray (it may be under the **^** arrow).

## Check the download

The release has a `.sha256` file. In PowerShell: `Get-FileHash .\Sway-<version>-win-x64.zip -Algorithm SHA256` and
compare it with the file's contents.

## Use

- Tray menu: Tracking on or off, Focus Lock, Calibrate, Settings, Diagnostics, Quit.
- Focus Lock freezes switching: **Ctrl+Alt+Shift+L** by default.
- Switching waits while a Sway window or the tray menu is in front.
- Windows running as administrator cannot be focused by a normal program, so Sway skips them. Do not run Sway as
  administrator to work around this: anyone who can write to the folder you unzipped it into could then run code with
  administrator rights. If you ever do, install it under `C:\Program Files\Sway`, which only administrators can change.

## Start with Windows

Turn on **Start with Windows** in Settings. It writes one value under
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run` and nothing else.

## Uninstall

Quit Sway from the tray, delete the folder, and delete `%AppData%\Sway` (settings and calibration) and `%LocalAppData%\Sway` (logs). If you turned on Start
with Windows, turn it off first in Settings.

## If something is wrong

Tray menu, Diagnostics, **Copy diagnostics** (no window titles, no images). The log is at `%LocalAppData%\Sway\Logs`.

## About, privacy and terms

Sway for Windows is made by Avi Malhotra and is free for personal use. It collects no data and has no network code. Read the [Privacy Policy](PRIVACY.md) and the [Terms of Service](TERMS.md); both also apply to Sway for macOS.
