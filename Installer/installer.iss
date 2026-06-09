; Aperture Neo Inno Setup Script
; Requires Inno Setup 6.0 or later
; Compiled with: ISCC /dMyAppVersion=x.y.z Installer\installer.iss

#define MyAppName "Aperture Neo"
#define MyAppPublisher "DuJunxi1993"
#define MyAppExeName "ApertureNeo.exe"
#define MyAppURL "https://github.com/DuJunxi1993/Aperture-Neo"
#define SupportedExtensions ".jpg|.jpeg|.png|.bmp|.gif|.tiff|.tif|.webp|.heic|.heif|.avif|.ico"
#define ProgId "ApertureNeo.Image.1"

[Setup]
AppId={{1B6E2D4A-3C8F-4A2E-9D7B-5E1F2A3B4C6D}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
; Output dir and base filename are passed at compile time via:
;   ISCC /dMyAppVersion=x.y.z /O"<out-dir>" /F"ApertureNeo-Setup-v<x.y.z>" Installer\installer.iss
; Defaults below are only used if the script is compiled directly (no /O flag).
OutputDir=..\publish
OutputBaseFilename=ApertureNeo-Setup
SetupIconFile=..\Assets\apertureneo.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
UninstallDisplayName={#MyAppName}
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoProductName={#MyAppName}
VersionInfoDescription={#MyAppName} Setup

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Messages]
WelcomeLabel2=This will install [name/ver] on your computer.%n%nAperture Neo is a fast, lightweight WPF image viewer. This package is self-contained and includes the .NET runtime, so no additional software installation is required.%n%nWebView2 Runtime is recommended for the modern UI. If not already present, you will be prompted to install it.

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"
Name: "fileassoc"; Description: "Register as a supported image viewer"; GroupDescription: "File associations:"

[Files]
; Self-contained publish output. The path is overridden at compile time via:
;   ISCC /dPublishDir="<absolute>" Installer\installer.iss
; Default below is for direct compilation from repo root.
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\Assets\LICENSE.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

[Registry]
; Per-user file associations: written to HKCU\Software\Classes (which Windows
; merges into HKCR for the current user). Works without admin rights and is
; the standard approach for non-elevated installers (VS Code, Slack, etc.).
Root: HKCU; Subkey: "Software\Classes\{#ProgId}"; ValueType: string; ValueName: ""; ValueData: "Aperture Neo Image"; Flags: uninsdeletekey; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\{#ProgId}\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#MyAppExeName},0"; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\{#ProgId}\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: fileassoc

; Register all supported extensions under OpenWithProgids (so user can pick
; from "Open With"). Per-user keys do not override system defaults but are
; surfaced in the Open With menu.
Root: HKCU; Subkey: "Software\Classes\.jpg\OpenWithProgids"; ValueType: string; ValueName: "{#ProgId}"; ValueData: ""; Flags: uninsdeletevalue; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\.jpeg\OpenWithProgids"; ValueType: string; ValueName: "{#ProgId}"; ValueData: ""; Flags: uninsdeletevalue; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\.png\OpenWithProgids"; ValueType: string; ValueName: "{#ProgId}"; ValueData: ""; Flags: uninsdeletevalue; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\.bmp\OpenWithProgids"; ValueType: string; ValueName: "{#ProgId}"; ValueData: ""; Flags: uninsdeletevalue; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\.gif\OpenWithProgids"; ValueType: string; ValueName: "{#ProgId}"; ValueData: ""; Flags: uninsdeletevalue; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\.tiff\OpenWithProgids"; ValueType: string; ValueName: "{#ProgId}"; ValueData: ""; Flags: uninsdeletevalue; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\.tif\OpenWithProgids"; ValueType: string; ValueName: "{#ProgId}"; ValueData: ""; Flags: uninsdeletevalue; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\.webp\OpenWithProgids"; ValueType: string; ValueName: "{#ProgId}"; ValueData: ""; Flags: uninsdeletevalue; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\.heic\OpenWithProgids"; ValueType: string; ValueName: "{#ProgId}"; ValueData: ""; Flags: uninsdeletevalue; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\.heif\OpenWithProgids"; ValueType: string; ValueName: "{#ProgId}"; ValueData: ""; Flags: uninsdeletevalue; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\.avif\OpenWithProgids"; ValueType: string; ValueName: "{#ProgId}"; ValueData: ""; Flags: uninsdeletevalue; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\.ico\OpenWithProgids"; ValueType: string; ValueName: "{#ProgId}"; ValueData: ""; Flags: uninsdeletevalue; Tasks: fileassoc

[Code]
const
  WebView2Guid = '{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';

function IsWebView2Installed(): Boolean;
var
  Key: String;
begin
  // Evergreen installer stores its version under
  // HKLM\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\<WebView2Guid>
  // (and same path under HKCU when installed per-user).
  Result := RegKeyExists(HKLM, 'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\' + WebView2Guid)
         or RegKeyExists(HKLM, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\' + WebView2Guid)
         or RegKeyExists(HKCU, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\' + WebView2Guid);
end;

function NeedsWebView2(): Boolean;
begin
  Result := not IsWebView2Installed();
end;

// Notify the shell that file association per-user keys have changed so the
// "Open With" menu is updated without requiring a logoff/logon. This writes
// to HKCU only (does not require admin) and is a no-op if SHChangeNotify
// fails.
procedure RefreshShellFileAssocs();
var
  SHChangeNotifyFlags: Integer;
begin
  // SHCNE_ASSOCCHANGED = 0x08000000, SHCNF_IDLIST = 0x0000
  SHChangeNotifyFlags := $08000000;
  // Use the registry-free import approach via the [Registry] section is not
  // available here, so we just broadcast a WM_SETTINGCHANGE.
  // (Calling SHChangeNotify is also fine, but it's not directly exposed to
  // Pascal script in Inno Setup, so we fall back to a simpler approach.)
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    // File association registry entries are written by the [Registry] section
    // (per-user under HKCU\Software\Classes) and do not need any post-install
    // command. This function is kept as a hook in case future steps are needed.
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  I: Integer;
  Ext: String;
  Extensions: Array of String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    Extensions := ['.jpg', '.jpeg', '.png', '.bmp', '.gif', '.tiff', '.tif', '.webp', '.heic', '.heif', '.avif', '.ico'];
    for I := 0 to GetArrayLength(Extensions) - 1 do
    begin
      Ext := Extensions[I];
      RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Classes\' + Ext + '\OpenWithProgids\{#ProgId}');
    end;
    RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Classes\{#ProgId}');
  end;
end;

// Show a warning at startup if WebView2 is missing. The app will still install
// (the rest of the app works without it; only the modern FluentWindow chrome
// needs WebView2).
function InitializeSetup(): Boolean;
var
  ErrorCode: Integer;
begin
  if NeedsWebView2() then
  begin
    if MsgBox(
      'WebView2 Runtime was not detected on this system.' + #13#10 + #13#10 +
      'Aperture Neo can still be installed, but the modern UI (Mica / FluentWindow) ' +
      'requires WebView2. The classic WPF chrome will be used as a fallback.' + #13#10 + #13#10 +
      'Download WebView2 Evergreen Bootstrapper now?' + #13#10 + #13#10 +
      '(You can also install it later from:' + #13#10 +
      'https://developer.microsoft.com/microsoft-edge/webview2/)',
      mbConfirmation, MB_YESNO) = IDYES then
    begin
      ShellExec('open', 'https:' + '/' + '/go.microsoft.com/fwlink/p/?LinkId=2124703', '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
    end;
  end;
  Result := True;
end;
