#!/usr/bin/env bash
# Static checks on installer/Gander.iss (no Inno Setup needed). Fails if a must-have property is missing.
set -euo pipefail
cd "$(dirname "$0")/.."
iss=installer/Gander.iss
fail=0
need() { # description, extended regex
  if grep -Eqi -- "$2" "$iss"; then echo "ok   $1"; else echo "FAIL $1"; fail=1; fi
}
ban() {
  if grep -Eqi -- "$2" "$iss"; then echo "FAIL $1"; fail=1; else echo "ok   $1"; fi
}

need "per-user by default" '^PrivilegesRequired=lowest'
need "can install per-machine via dialog" '^PrivilegesRequiredOverridesAllowed=dialog'
need "x64 only" '^ArchitecturesAllowed=x64compatible'
need "version define" '^AppVersion=\{#AppVersion\}'
need "version required" '^#ifndef AppVersion'
need "source dir define" '\{#SourceDir\}'
need "fixed AppId" '^AppId=\{\{[0-9A-F-]{36}\}'
need "output name" '^OutputBaseFilename=Gander-\{#AppVersion\}-win-x64-setup'
need "lzma2" '^Compression=lzma2'
need "modern wizard" '^WizardStyle=modern'
need "min Windows version" '^MinVersion=10\.0\.19041'
need "icon" '^SetupIconFile=.*Gander\.ico'
need "startup task unchecked" '^Name: "startup".*Flags: unchecked'
need "desktop icon unchecked" '^Name: "desktopicon".*Flags: unchecked'
need "Run value removed on uninstall" 'uninsdeletevalue'
need "uninstall closes Gander" 'InitializeUninstall'
need "uninstall asks about settings, default No" 'MB_YESNO or MB_DEFBUTTON2'
need "deletes %AppData%\\Gander" "userappdata\}\\\\Gander"
need "deletes %LocalAppData%\\Gander" "localappdata\}\\\\Gander"
need "launch after install" 'postinstall'

# The Run value name must match the app.
cs=src/Gander.Platform.Windows/Settings/RegistryLoginItem.cs
app_name=$(sed -n 's/.*public const string ValueName = "\([^"]*\)".*/\1/p' "$cs")
iss_name=$(sed -n 's/^#define RunValueName "\([^"]*\)".*/\1/p' "$iss")
if [[ -n "$app_name" && "$app_name" == "$iss_name" ]]; then echo "ok   Run value name matches app ($app_name)"; else echo "FAIL Run value name (app '$app_name', installer '$iss_name')"; fail=1; fi
need "Run value is the quoted exe path" 'ValueData: """\{app\}\\\{#AppExe\}"""'
need "Run key is HKCU" 'Root: HKCU; Subkey: "Software\\Microsoft\\Windows\\CurrentVersion\\Run"'

ban "no network use" 'https?://|DownloadTemporaryFile|idp|InitializeDownload|WinHttp|MSXML|Msxml2|XMLHTTP|curl|Invoke-WebRequest'
ban "no external script downloads" 'powershell'

[[ -f installer/before-you-install.txt ]] && echo "ok   privacy note present" || { echo "FAIL privacy note missing"; fail=1; }
need "privacy note page wired" '^InfoBeforeFile=before-you-install\.txt'

if [[ $fail -ne 0 ]]; then echo "Installer check FAILED"; exit 1; fi
echo "Installer check passed"
