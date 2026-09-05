#ifndef MyAppVersion
  #define MyAppVersion "1.2.0"
#endif

#define MyAppName "ARDOR Mouse Battery Tray"
#define MyAppExeName "ArdorBatteryTray.exe"

[Setup]
AppId={{5F5D3787-0434-4F9D-BFC4-4C488405071C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher=ArdorBatteryTray contributors
DefaultDirName={localappdata}\Programs\ArdorBatteryTray
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
MinVersion=10.0.17763
OutputDir=dist
OutputBaseFilename=ArdorBatteryTray-Setup-v{#MyAppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
SetupIconFile=app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
AppMutex=Local\ArdorChimeraBatteryTray
CloseApplications=yes
RestartApplications=no
ArchitecturesInstallIn64BitMode=x64compatible
VersionInfoVersion={#MyAppVersion}.0
VersionInfoProductName={#MyAppName}
VersionInfoDescription=Installer for {#MyAppName}

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "startup"; Description: "{cm:StartupTask}"; GroupDescription: "{cm:AdditionalTasks}"; Flags: unchecked
Name: "desktopicon"; Description: "{cm:DesktopIconTask}"; GroupDescription: "{cm:AdditionalTasks}"; Flags: unchecked

[CustomMessages]
english.StartupTask=Start with Windows
english.DesktopIconTask=Create a desktop shortcut
english.AdditionalTasks=Additional options:
english.LaunchProgram=Launch %1
russian.StartupTask=Запускать вместе с Windows
russian.DesktopIconTask=Создать ярлык на рабочем столе
russian.AdditionalTasks=Дополнительно:
russian.LaunchProgram=Запустить %1

[Files]
Source: "ArdorBatteryTray.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "README.ru.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "LICENSE.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "THIRD_PARTY_NOTICES.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "ArdorChimeraBatteryTray"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: startup

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent
