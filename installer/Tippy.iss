#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

[Setup]
AppId={{7BB9508A-AACD-4B1D-99E5-BD2D96A52A46}
AppName=Tippy
AppVersion={#AppVersion}
AppPublisher=TerkWerX
AppPublisherURL=https://TerkWerX.com
AppSupportURL=https://github.com/TerkWerX/TIPPY/issues
AppUpdatesURL=https://github.com/TerkWerX/TIPPY/releases/latest
DefaultDirName={localappdata}\Programs\Tippy
DefaultGroupName=Tippy
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\dist\release
OutputBaseFilename=Tippy-{#AppVersion}-Setup-x64
SetupIconFile=..\src\Tippy.App\Assets\Icons\tippy.ico
UninstallDisplayIcon={app}\Tippy.exe
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
AppMutex=Local\Tippy.FootControlMacros

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "..\dist\Tippy-win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Tippy"; Filename: "{app}\Tippy.exe"
Name: "{autodesktop}\Tippy"; Filename: "{app}\Tippy.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Tippy.exe"; Description: "Launch Tippy"; Flags: nowait postinstall skipifsilent
