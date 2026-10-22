#define MyAppName "PickmeTurn"
#define MyAppVersion "1.2.6"
#define MyAppPublisher "PickmeTurn"
#define MyAppExeName "PickmeTurn.exe"

[Setup]
AppId={{8E7B8C6D-2E7A-4D2A-9D35-7A8E2D6F5C41}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppComments=Windows client for PickmeTurn
DefaultDirName={autopf}\PickmeTurn
DirExistsWarning=no
DefaultGroupName=PickmeTurn
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
OutputDir=..\installer-output
OutputBaseFilename=PickmeTurn-Setup-{#MyAppVersion}
SetupIconFile=..\Assets\FreeTurn.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ChangesAssociations=no
CloseApplications=yes
CloseApplicationsFilter=PickmeTurn.exe
RestartApplications=no
Uninstallable=yes
UninstallDisplayName=PickmeTurn

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Создать ярлык на рабочем столе"; GroupDescription: "Дополнительные ярлыки:"; Flags: unchecked

[Files]
Source: "..\publish\PickmeTurn.exe"; DestDir: "{app}"; Flags: ignoreversion restartreplace
Source: "Cleanup-WireGuardServices.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "Request-PickmeTurnShutdown.ps1"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\PickmeTurn"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{commondesktop}\PickmeTurn"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[InstallDelete]
Type: files; Name: "{app}\PickmeTurn.exe"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Запустить PickmeTurn"; Flags: nowait postinstall skipifsilent runascurrentuser

[UninstallDelete]

[Code]
var
  KeepProfiles: Boolean;

procedure CurUninstallStepChanged(AStep: TUninstallStep);
var
  Dummy: Integer;
begin
  if AStep = usUninstall then
  begin
    if not Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
      '-NoProfile -ExecutionPolicy Bypass -File "' + ExpandConstant('{app}\Request-PickmeTurnShutdown.ps1') + '"',
      '', SW_HIDE, ewWaitUntilTerminated, Dummy) then
    begin
      MsgBox('Could not request PickmeTurn shutdown. Close the application and retry uninstallation.', mbError, MB_OK);
      Abort;
    end;

    if Dummy <> 0 then
    begin
      MsgBox('PickmeTurn did not shut down cleanly. Uninstallation has been cancelled; close the application and retry.', mbError, MB_OK);
      Abort;
    end;

    if not Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
      '-NoProfile -ExecutionPolicy Bypass -File "' + ExpandConstant('{app}\Cleanup-WireGuardServices.ps1') + '"',
      '', SW_HIDE, ewWaitUntilTerminated, Dummy) then
    begin
      MsgBox('Could not start WireGuard cleanup. Uninstallation has been cancelled.', mbError, MB_OK);
      Abort;
    end;

    if Dummy <> 0 then
    begin
      MsgBox('WireGuard cleanup failed. Uninstallation has been cancelled to avoid leaving a stale tunnel service.', mbError, MB_OK);
      Abort;
    end;
    KeepProfiles := MsgBox('Удалить сохранённые профили PickmeTurn?' + #13#10 + #13#10 +
                           'Да — удалить профили и настройки подключения.' + #13#10 +
                           'Нет — оставить профили для следующей установки.',
                           mbConfirmation, MB_YESNO) <> IDYES;
  end
  else if AStep = usPostUninstall then
  begin
    DelTree(ExpandConstant('{app}'), True, True, True);
    // Runtime contains only extracted FreeTurn/WireGuard binaries and logs.
    // Remove it even when the user chooses to preserve profiles/settings.
    DelTree(ExpandConstant('{localappdata}\PickmeTurn\Runtime'), True, True, True);
    if not KeepProfiles then
      DelTree(ExpandConstant('{localappdata}\PickmeTurn'), True, True, True);
  end;
end;



