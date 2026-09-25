#define MyAppName "PickmeTurn"
#define MyAppVersion "1.1.0"
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
RestartApplications=no
Uninstallable=yes

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

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Запустить PickmeTurn"; Flags: nowait postinstall skipifsilent runascurrentuser

[UninstallDelete]
; Runtime contains temporary extracted FreeTurn/WireGuard executables.
Type: filesandordirs; Name: "{localappdata}\PickmeTurn\Runtime"
