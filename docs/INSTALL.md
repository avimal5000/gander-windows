# Installing Gander for Windows

Gander is a quiet tray program. It uses your webcam to see which way your head is turned and moves keyboard focus to the
last window you used on that monitor. Nothing leaves your PC: no network code, no saved video or pictures.

## Install

1. Unzip the whole `Gander-<version>-win-x64.zip` into a folder you keep, for example `C:\Users\<you>\Gander` or
   `C:\Program Files\Gander`. Keep the files together. Gander needs the `models` folder and the `.dll` files next to
   `Gander.exe`.
2. Run `Gander.exe`.
3. This build is **unsigned**, so Windows SmartScreen shows "Windows protected your PC" the first time. Choose
   **More info**, then **Run anyway**. If the file was marked as downloaded, you can also right-click the zip before
   unzipping, choose Properties, tick **Unblock**, then OK.
4. The "Set up Gander" window opens on first launch. Allow camera access when Windows asks (Settings, Privacy &
   security, Camera, "Let desktop apps access your camera" must be on).
5. Follow the steps: check your framing, then look at each monitor twice when the green "Look here" marker shows.
6. Choose **Start tracking**. Gander lives in the tray (it may be under the **^** arrow).

## Installer

The release also has `Gander-<version>-win-x64-setup.exe`. Use it instead of the zip if you want Start Menu entries and a
normal uninstall.

- **Installer or zip?** Both contain the same app. The installer puts Gander in `%LocalAppData%\Programs\Gander` for your
  account only (no administrator prompt); the first page lets you choose "all users" (`C:\Program Files\Gander`)
  instead. The zip is portable: unzip anywhere and delete the folder when done.
- **Unsigned.** Windows SmartScreen warns the first time, as for the zip: **More info**, then **Run anyway**. Check the
  download against the `.sha256` file first.
- Options: a desktop shortcut, and **Start Gander when I sign in to Windows**. Both are off by default. The second sets the
  same value as Gander's own **Start with Windows** setting, so Settings shows it correctly.
- **Silent install:** `Gander-<version>-win-x64-setup.exe /VERYSILENT /SUPPRESSMSGBOXES /CURRENTUSER` (use `/ALLUSERS`
  for a machine-wide install from an administrator prompt; `/DIR="C:\path"` picks the folder).
- **Uninstall:** Settings, Apps, Installed apps, Gander, Uninstall. It closes Gander if it is running, removes the files and
  the sign-in entry, then asks whether to delete your settings (`%AppData%\Gander`) and logs (`%LocalAppData%\Gander`); the
  default is No. Silent uninstall: run `unins000.exe /VERYSILENT /SUPPRESSMSGBOXES` from the install folder; it keeps your
  settings and logs.

## Check the download

The release has a `.sha256` file for each download. In PowerShell: `Get-FileHash .\Gander-<version>-win-x64.zip -Algorithm SHA256` and
compare it with the file's contents.

## Use

- Click the goose in the tray to open the Gander window: the status, Tracking and Focus Lock (each says what it
  does), the camera, and pages for Calibrate, Settings and Troubleshooting (the Welcome Tour and the test tools). Back (or Esc) returns
  to the page before.
- Right-click the goose for the quick menu: Open Gander, Tracking, Focus Lock, camera, Calibrate, Settings,
  Troubleshooting (the Welcome Tour, the test windows and Diagnostics), Quit. Hover over a row to see what it does.
- Focus Lock freezes switching: **Ctrl+Alt+Shift+L** by default.
- Switching waits while a Gander window or the tray menu is in front.
- Windows running as administrator cannot be focused by a normal program, so Gander skips them. Do not run Gander as
  administrator to work around this: anyone who can write to the folder you unzipped it into could then run code with
  administrator rights. If you ever do, install it under `C:\Program Files\Gander`, which only administrators can change.

## Start with Windows

Turn on **Start with Windows** in Settings. It writes one value under
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run` and nothing else.

## Uninstall

Quit Gander from the tray, delete the folder, and delete `%AppData%\Gander` (settings and calibration) and `%LocalAppData%\Gander` (logs). If you turned on Start
with Windows, turn it off first in Settings.

## If something is wrong

Tray menu, Diagnostics, **Copy diagnostics** (no window titles, no images). The log is at `%LocalAppData%\Gander\Logs`.

## About, privacy and terms

Gander for Windows is made by Avi Malhotra and is free for personal use. It collects no data and has no network code. Read the [Privacy Policy](PRIVACY.md) and the [Terms of Service](TERMS.md); both also apply to Gander for macOS.
