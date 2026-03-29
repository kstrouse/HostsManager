#define AppName "Hosts Manager"
#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#ifndef PublishDir
  #error PublishDir must be supplied on the ISCC command line.
#endif
#ifndef InstallerOutputDir
  #define InstallerOutputDir AddBackslash(SourcePath) + "..\..\artifacts\installer"
#endif
#define SetupIconSource AddBackslash(SourcePath) + "..\..\src\HostsManager.Desktop\Assets\hosts-manager.ico"

[Setup]
AppId={{E4A579B3-0C39-4FF4-A3B5-0B7BEE6970E0}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=Hosts Manager
DefaultDirName={autopf}\Hosts Manager
DefaultGroupName=Hosts Manager
AllowNoIcons=yes
DisableProgramGroupPage=yes
Compression=lzma
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\HostsManager.Desktop.exe
OutputDir={#InstallerOutputDir}
OutputBaseFilename=HostsManager-{#AppVersion}-Setup
SetupIconFile={#SetupIconSource}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional icons:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Hosts Manager"; Filename: "{app}\HostsManager.Desktop.exe"
Name: "{autodesktop}\Hosts Manager"; Filename: "{app}\HostsManager.Desktop.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\HostsManager.Desktop.exe"; Description: "Launch Hosts Manager"; Flags: nowait postinstall skipifsilent
