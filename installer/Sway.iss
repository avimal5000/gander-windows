; Inno Setup 6 script for Sway for Windows.
; Build:  ISCC /DAppVersion=0.1.0 /DSourceDir=..\artifacts\Sway installer\Sway.iss
; Relative paths here are relative to this script's folder (installer\).

#ifndef AppVersion
  #error Pass the version: ISCC /DAppVersion=0.1.0 /DSourceDir=..\artifacts\Sway installer\Sway.iss
#endif
#ifndef SourceDir
  #error Pass the published folder: ISCC /DAppVersion=0.1.0 /DSourceDir=..\artifacts\Sway installer\Sway.iss
#endif

#define AppName "Sway"
#define AppExe "Sway.exe"
; Same value name the app writes (RegistryLoginItem.ValueName).
#define RunValueName "Sway"

[Setup]
; Fixed GUID. A literal "{" is written "{{" in Inno Setup.
AppId={{8DD57F5A-F79E-4C44-8534-14A2A0E3601E}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=Avi Malhotra
DefaultDirName={autopf}\Sway
; Per-user by default ({localappdata}\Programs\Sway). The dialog offers "for all users"; /CURRENTUSER and /ALLUSERS work too.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
DisableProgramGroupPage=yes
WizardStyle=modern
Compression=lzma2
SolidCompression=yes
SetupIconFile=..\src\Sway.App\Assets\Sway.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
OutputDir=..\artifacts
OutputBaseFilename=Sway-{#AppVersion}-win-x64-setup
InfoBeforeFile=before-you-install.txt
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Messages]
WizardInfoBefore=Before you install
InfoBeforeLabel=Please read this before you install Sway.

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startup"; Description: "Start Sway when I sign in to Windows"; GroupDescription: "Startup:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Sway"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\Sway"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; Same value name and quoted path as RegistryLoginItem, so Settings shows "Start with Windows" as on.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#RunValueName}"; ValueData: """{app}\{#AppExe}"""; Flags: uninsdeletevalue; Tasks: startup

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,Sway}"; Flags: nowait postinstall skipifsilent

[Code]
function IsSwayRunning: Boolean;
var
  ResultCode: Integer;
begin
  Result := False;
  if Exec(ExpandConstant('{sys}\cmd.exe'),
          '/C tasklist /FI "IMAGENAME eq Sway.exe" | find /I "Sway.exe"',
          '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    Result := (ResultCode = 0);
end;

procedure KillSway;
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM Sway.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(500);
end;

function InitializeUninstall: Boolean;
begin
  Result := True;
  if IsSwayRunning then
  begin
    if UninstallSilent then
      KillSway
    else if MsgBox('Sway is running. Close it now and continue uninstalling?',
                   mbConfirmation, MB_YESNO) = IDYES then
      KillSway
    else
      Result := False;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    { Also remove the Run value when the user turned it on later in Sway's Settings. }
    RegDeleteValue(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Run', '{#RunValueName}');
  end
  else if CurUninstallStep = usPostUninstall then
  begin
    { Default is No. Silent uninstalls keep the data. }
    if not UninstallSilent then
    begin
      if MsgBox('Also delete your Sway settings and calibration (' + ExpandConstant('{userappdata}\Sway') +
                ') and logs (' + ExpandConstant('{localappdata}\Sway') + ')?',
                mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      begin
        DelTree(ExpandConstant('{userappdata}\Sway'), True, True, True);
        DelTree(ExpandConstant('{localappdata}\Sway'), True, True, True);
      end;
    end;
  end;
end;
