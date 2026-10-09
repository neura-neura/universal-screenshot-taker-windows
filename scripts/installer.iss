#ifndef SourceDir
 #define SourceDir "..\dist\portable-x64"
#endif
#ifndef OutputDir
 #define OutputDir "..\dist"
#endif
[Setup]
AppId={{C5E4C55C-7AF0-46C6-BA02-49BBA99D9D5A}
AppName=Universal Screenshot Taker
AppVersion=1.1.5
AppPublisher=neura-neura
AppPublisherURL=https://github.com/neura-neura
DefaultDirName={localappdata}\Programs\Universal Screenshot Taker
DefaultGroupName=Universal Screenshot Taker
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir={#OutputDir}
OutputBaseFilename=UniversalScreenshotTaker-Setup-x64
SetupIconFile={#SourceDir}\Assets\AppIcon.ico
UninstallDisplayIcon={app}\UniversalCapture.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
LicenseFile=..\LICENSE
[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"
[Tasks]
Name: "desktopicon"; Description: "Crear acceso directo en el escritorio"; Flags: unchecked
[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
[Icons]
Name: "{group}\Universal Screenshot Taker"; Filename: "{app}\UniversalCapture.exe"
Name: "{autodesktop}\Universal Screenshot Taker"; Filename: "{app}\UniversalCapture.exe"; Tasks: desktopicon
[Run]
Filename: "{app}\UniversalCapture.exe"; Description: "Abrir Universal Screenshot Taker"; Flags: nowait postinstall skipifsilent

