; A plain Setup.exe for people who aren't on the Store: Next, Next, Finish, no administrator and no certificate to
; trust. It installs the portable exe for this PC's architecture under %LOCALAPPDATA%\Programs\Outatime, adds a
; Start menu shortcut and an uninstaller (Settings → Apps). Data stays in %LOCALAPPDATA%\Outatime, so uninstalling
; or upgrading keeps the logbook. scripts/package.ps1 compiles it with Inno Setup 6:
;
;   iscc /DAppVersion=1.1.0 /DArtifacts=<windows\artifacts> package\Outatime.iss

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef Artifacts
  #define Artifacts "..\artifacts"
#endif

[Setup]
AppId={{6F2B9D41-3C7A-4E58-9A1D-0B8E5C7F2A63}
AppName=Outatime
AppVersion={#AppVersion}
AppVerName=Outatime {#AppVersion}
AppPublisher=David Barkan
AppPublisherURL=https://github.com/mrbarkan/Outatime
AppSupportURL=https://mrbarkan.github.io/Outatime/manual/
VersionInfoVersion={#AppVersion}
DefaultDirName={autopf}\Outatime
DefaultGroupName=Outatime
DisableProgramGroupPage=yes
DisableDirPage=yes
; Per user: no UAC prompt, installs under %LOCALAPPDATA%\Programs.
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible or arm64
ArchitecturesInstallIn64BitMode=x64compatible or arm64
MinVersion=10.0.19041
OutputDir={#Artifacts}
OutputBaseFilename=Outatime-{#AppVersion}-Setup
SetupIconFile=..\src\Outatime\Assets\Outatime.ico
UninstallDisplayIcon={app}\Outatime.exe
UninstallDisplayName=Outatime
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ShowLanguageDialog=no
; An upgrade closes the running copy in the tray first, and the finish page opens the new one.
CloseApplications=force
RestartApplications=no

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "es"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "pt"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[CustomMessages]
en.StartAtLogin=Open Outatime when I sign in to Windows
es.StartAtLogin=Abrir Outatime al iniciar sesión en Windows
pt.StartAtLogin=Abrir o Outatime ao entrar no Windows
en.Launch=Open Outatime (it lives in the notification area, next to the clock)
es.Launch=Abrir Outatime (vive en el área de notificación, junto al reloj)
pt.Launch=Abrir o Outatime (ele fica na área de notificação, ao lado do relógio)

[Tasks]
Name: "startup"; Description: "{cm:StartAtLogin}"
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#Artifacts}\single-x64\Outatime.exe"; DestDir: "{app}"; Check: not IsArm64; Flags: ignoreversion
Source: "{#Artifacts}\single-arm64\Outatime.exe"; DestDir: "{app}"; Check: IsArm64; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\Outatime"; Filename: "{app}\Outatime.exe"
Name: "{autodesktop}\Outatime"; Filename: "{app}\Outatime.exe"; Tasks: desktopicon

[Registry]
; The same Run value Settings → General → Open at Login writes, so the app's switch shows it and can turn it off.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Outatime"; \
  ValueData: """{app}\Outatime.exe"""; Tasks: startup; Flags: uninsdeletevalue
; Whatever the box said at install, uninstalling removes the value the app may have written later.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "Outatime"; ValueType: none; \
  Flags: uninsdeletevalue dontcreatekey

[Run]
Filename: "{app}\Outatime.exe"; Description: "{cm:Launch}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{cmd}"; Parameters: "/C taskkill /IM Outatime.exe /F"; Flags: runhidden; RunOnceId: "StopOutatime"
