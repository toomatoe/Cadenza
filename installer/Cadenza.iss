#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#ifndef FileVersion
  #define FileVersion "0.1.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\artifacts\windows-x64"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts"
#endif

[Setup]
AppId={{7A8D8E20-F2D8-4D71-960A-887C8E78D56B}
AppName=Cadenza
AppVersion={#AppVersion}
VersionInfoVersion={#FileVersion}
AppPublisher=Cadenza
AppPublisherURL=https://github.com/toomatoe/Cadenza
AppSupportURL=https://github.com/toomatoe/Cadenza/issues
DefaultDirName={localappdata}\Programs\Cadenza
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir={#OutputDir}
OutputBaseFilename=Cadenza-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\Cadenza.Desktop.exe
CloseApplications=yes
RestartApplications=no
SetupLogging=yes

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"

[Icons]
Name: "{userprograms}\Cadenza"; Filename: "{app}\Cadenza.Desktop.exe"; WorkingDir: "{app}"
Name: "{userdesktop}\Cadenza"; Filename: "{app}\Cadenza.Desktop.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\Cadenza.Desktop.exe"; Description: "Open Cadenza"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent
