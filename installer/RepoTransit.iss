#ifndef AppVersion
  #error AppVersion must be supplied by build-installer.ps1
#endif
#ifndef SourceDir
  #error SourceDir must be supplied by build-installer.ps1
#endif
#ifndef OutputDir
  #error OutputDir must be supplied by build-installer.ps1
#endif

[Setup]
AppId={{5E3D4A8B-0FD3-4BC5-AE31-C9E9C33892D0}
AppName=仓渡 RepoTransit
AppVersion={#AppVersion}
AppPublisher=NanoAgentCode
AppPublisherURL=https://github.com/NanoAgentCode/Transit
DefaultDirName={localappdata}\Programs\RepoTransit
DefaultGroupName=仓渡
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
AppMutex=Global\RepoTransit.SingleInstance
UninstallDisplayIcon={app}\RepoTransit.exe
SetupIconFile=..\src\RepoTransit\Assets\RepoTransit.ico
OutputDir={#OutputDir}
OutputBaseFilename=RepoTransit-{#AppVersion}-win-x64-setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "chinesesimp"; MessagesFile: "ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"

[Icons]
Name: "{group}\仓渡"; Filename: "{app}\RepoTransit.exe"
Name: "{autodesktop}\仓渡"; Filename: "{app}\RepoTransit.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\RepoTransit.exe"; Description: "启动仓渡"; Flags: nowait postinstall skipifsilent
