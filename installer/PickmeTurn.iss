#define MyAppName "PickmeTurn"
#define MyAppVersion "1.2.2"
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
    Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM {#MyAppExeName}', '', SW_HIDE, ewWaitUntilTerminated, Dummy);
    KeepProfiles := MsgBox('РЈРґР°Р»РёС‚СЊ СЃРѕС…СЂР°РЅС‘РЅРЅС‹Рµ РїСЂРѕС„РёР»Рё PickmeTurn?' + #13#10 + #13#10 +
                           'Р”Р° вЂ” СѓРґР°Р»РёС‚СЊ РїСЂРѕС„РёР»Рё Рё РЅР°СЃС‚СЂРѕР№РєРё РїРѕРґРєР»СЋС‡РµРЅРёСЏ.' + #13#10 +
                           'РќРµС‚ вЂ” РѕСЃС‚Р°РІРёС‚СЊ РїСЂРѕС„РёР»Рё РґР»СЏ СЃР»РµРґСѓСЋС‰РµР№ СѓСЃС‚Р°РЅРѕРІРєРё.',
                           mbConfirmation, MB_YESNO) <> IDYES;
  end
  else if AStep = usPostUninstall then
  begin
    DelTree(ExpandConstant('{app}'), True, True, True);
    if not KeepProfiles then
      DelTree(ExpandConstant('{localappdata}\PickmeTurn'), True, True, True);
  end;
end;


