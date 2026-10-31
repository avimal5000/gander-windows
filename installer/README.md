# Gander installer

`Sway.iss` is an [Inno Setup 6](https://jrsoftware.org/isinfo.php) script. It packs the published self-contained folder
into `Gander-<version>-win-x64-setup.exe`.

- Per-user by default (`%LocalAppData%\Programs\Gander`, no administrator prompt). The first page offers "all users"
  (`C:\Program Files\Gander`).
- Optional desktop shortcut and "Start Gander when I sign in to Windows" (both off). The second writes the same
  `HKCU\...\Run` value `Gander` with the quoted exe path that the app writes, so Settings shows it correctly.
- Uninstall closes a running Gander, removes the files and the Run value, then asks (default No) whether to delete
  settings (`%AppData%\Gander`) and logs (`%LocalAppData%\Gander`). Silent uninstalls keep them.
- No network use anywhere. The installer is unsigned (SmartScreen warns once).

## Build in CI

Inno Setup 6 is preinstalled on `windows-latest` (`C:\Program Files (x86)\Inno Setup 6\ISCC.exe`; otherwise
`choco install innosetup -y`). After `dotnet publish ... --output artifacts/Sway` and the notices copy step:

```powershell
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" "/DAppVersion=$version" "/DSourceDir=..\artifacts\Gander" installer\Sway.iss
```

Output: `artifacts\Gander-<version>-win-x64-setup.exe`. Write a `.sha256` next to it and upload both to the release.
`/DSourceDir` is relative to `installer\` (an absolute path also works). `AppVersion` is any version text, for example
`0.2.0` or `0.2.0-rc.1`.

## Test locally on Windows

```powershell
dotnet publish src\Sway.App\Sway.App.csproj -c Release -r win-x64 --self-contained true -o artifacts\Gander
cd installer
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" /DAppVersion=0.1.0 /DSourceDir=..\artifacts\Gander Sway.iss
```

Then run `..\artifacts\Gander-0.1.0-win-x64-setup.exe`. Check: install, tick the sign-in task and confirm Gander's Settings
shows "Start with Windows" on, run Gander and uninstall (it should offer to close Gander, then ask about settings).
Silent: `Gander-0.1.0-win-x64-setup.exe /VERYSILENT /SUPPRESSMSGBOXES /CURRENTUSER`.

## Static check (any OS)

`scripts/check-installer.sh` greps the script for the must-have properties. It cannot compile it.
