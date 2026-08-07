; Inno Setup script for USB DAQ — produces a single-click Setup.exe
; Version is passed in from CI via /DMyAppVersion=x.y.z

#ifndef MyAppVersion
  #define MyAppVersion "0.0.0"
#endif

#define MyAppName "USB DAQ"
#define MyAppExeName "UsbDaq.App.exe"
#define MyAppPublisher "Kirby"

[Setup]
AppId={{8F3B2A14-6C7D-4E52-9A1B-USBDAQ612000}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
; Per-user install — no admin/UAC prompt
PrivilegesRequired=lowest
DefaultDirName={localappdata}\Programs\UsbDaq612
DisableProgramGroupPage=yes
DisableDirPage=yes
OutputBaseFilename=USB-DAQ-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupIconFile=icon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
; Close the running app automatically during upgrades
CloseApplications=yes
RestartApplications=no
SetupLogging=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional icons:"

[Files]
; Everything from the published self-contained app folder
Source: "..\publish\app\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{autoprograms}\USB DAQ"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\USB DAQ"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; Offer to launch after install; silent during auto-update
Filename: "{app}\{#MyAppExeName}"; Description: "Launch USB DAQ"; Flags: nowait postinstall skipifsilent
