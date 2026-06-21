; Inno Setup script for Auto MIDI Player
; Build with: iscc /DAppVersion=7.10.4 installer\AutoMidiPlayer.iss
; (the build-installer.ps1 script passes the version and source dir automatically)

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

; Folder containing the self-contained published app (set by build script).
; Defaults to the path the build script uses.
#ifndef SourceDir
  #define SourceDir "..\dist\installer-app"
#endif

#define AppName "Auto MIDI Player"
#define AppPublisher "Jed556"
#define AppURL "https://github.com/Jed556/AutoMidiPlayer"
#define AppExeName "Auto MIDI Player.exe"

[Setup]
; A stable, unique GUID identifies this application for upgrades/uninstall.
AppId={{8F3A6C2E-2D4B-4E9A-9C7E-6B1F0A2D5C81}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}/issues
AppUpdatesURL={#AppURL}/releases
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
; Per-machine install (Program Files) requires admin. Use lowest for per-user.
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=..\dist
OutputBaseFilename=AutoMidiPlayer-Setup-{#AppVersion}
SetupIconFile=..\AutoMidiPlayer.WPF\Resources\logo.ico
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName}
WizardStyle=modern
Compression=lzma2/ultra64
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE
MinVersion=10.0.17763

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Recursively include everything from the published self-contained app folder.
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
