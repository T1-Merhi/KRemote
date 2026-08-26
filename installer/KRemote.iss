; KRemote installer script (Inno Setup 6)
;
; Build it with installer\build-installer.ps1, which publishes the app first --
; this script packages whatever is already in the publish\ folder and will fail
; the compile if that folder is missing.
;
; Per-user install: no admin prompt to install, so {autopf} resolves to
; %LocalAppData%\Programs. The one elevation in the whole flow is the optional
; firewall rule, which asks for itself.

#define AppName        "KRemote"
#ifndef AppVersion
  #define AppVersion   "1.0.0"
#endif
#define AppPublisher   "Hussein Merhi"
#define AppExeName     "KRemote.exe"
#define AppUrl         "https://github.com/"

[Setup]
AppId={{8F3A2C41-9B57-4D6E-A1F0-2E6C5B7D9143}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppSupportURL={#AppUrl}
VersionInfoVersion={#AppVersion}

; Per-user, so Windows never asks for admin just to copy files.
PrivilegesRequired=lowest
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=auto
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName} {#AppVersion}

; The payload is self-contained (it carries .NET with it), so 64-bit only.
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

OutputDir=..\dist
OutputBaseFilename={#AppName}-Setup-{#AppVersion}
SetupIconFile=..\{#AppName}.ico
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
LicenseFile=..\LICENSE

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"
Name: "firewallrule"; Description: "Allow KRemote through Windows Firewall (needs one admin confirmation)"; GroupDescription: "Network:"

[Files]
; Everything the publish step produced: the app plus its bundled .NET runtime.
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "firewall.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md";  DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE";    DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"; IconFilename: "{app}\{#AppExeName}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; IconFilename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
; firewall.ps1 elevates itself, which is why this line does not need admin.
Filename: "powershell.exe"; \
  Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\firewall.ps1"" -Action Add -ExePath ""{app}\{#AppExeName}"""; \
  StatusMsg: "Adding the Windows Firewall rule for port 5555..."; \
  Flags: runhidden waituntilterminated; Tasks: firewallrule

Filename: "{app}\{#AppExeName}"; Description: "Launch {#AppName}"; \
  Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "powershell.exe"; \
  Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\firewall.ps1"" -Action Remove"; \
  Flags: runhidden waituntilterminated; RunOnceId: "RemoveKRemoteFirewallRule"

[UninstallDelete]
; Saved inbox messages live in %AppData%\KRemote and are deliberately left
; behind, the same way any app leaves user documents alone.
Type: dirifempty; Name: "{app}"
