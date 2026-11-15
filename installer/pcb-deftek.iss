; PCB Deftek installer definition.
; Compiled by build/package.ps1, which passes AppVersion and SourceDir:
;   ISCC.exe /DAppVersion=0.10.0 /DSourceDir=...\artifacts\pcb-deftek-0.10.0-win-x64 /O...\artifacts installer\pcb-deftek.iss
; Installs per user (no elevation) and never writes to user data directories.

#define AppName "PCB Deftek"
#define AppExeName "pcb-deftek.exe"
#define AppPublisher "Ricky Kiva"
#define AppId "{{A3F1C0D2-6B8E-4E37-9C2A-7D5B1E9F4C60}"

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

#ifndef SourceDir
  #define SourceDir "..\artifacts\pcb-deftek-" + AppVersion + "-win-x64"
#endif

[Setup]
AppId={#AppId}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={localappdata}\Programs\PCB Deftek
DefaultGroupName=PCB Deftek
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\artifacts
OutputBaseFilename=PCB-Deftek-{#AppVersion}-setup
SetupIconFile=..\Assets\app-icon.ico
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
