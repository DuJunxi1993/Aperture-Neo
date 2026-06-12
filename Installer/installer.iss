; Aperture Neo Inno Setup Script
; Requires Inno Setup 6.0 or later
; Compiled with: ISCC /dMyAppVersion=x.y.z /dPublishDir="<abs>" Installer\installer.iss
;
; Per-user, no-admin installer:
;   - PrivilegesRequired=user
;   - Installs to {localappdata}\Programs\ApertureNeo
;   - All registry keys under HKCU
;   - Fonts registered to %LOCALAPPDATA%\Microsoft\Windows\Fonts\ + HKCU\...\Fonts
;   - Detects an existing installation of the same AppId and silently
;     uninstalls it (after user confirmation) before installing the new
;     version. Settings/cache are preserved by the app's MigrateLegacyData().

#define MyAppName "Aperture Neo"
#define MyAppPublisher "DuJunxi1993"
#define MyAppExeName "ApertureNeo.exe"
#define MyAppURL "https://github.com/DuJunxi1993/Aperture-Neo"
#define SupportedExtensions ".jpg|.jpeg|.png|.bmp|.gif|.tiff|.tif|.webp|.heic|.heif|.avif|.ico"
#define ProgId "ApertureNeo.Image.1"
#define FontDir "Fonts\HarmonyOS_Sans_SC"

[Setup]
AppId={{1B6E2D4A-3C8F-4A2E-9D7B-5E1F2A3B4C6D}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
; Per-user install path (no admin required). {userpf} resolves to
; %LOCALAPPDATA%\Programs on Windows 7+, the standard per-user install location.
DefaultDirName={userpf}\{#MyAppName}
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
; Per-user, never elevate. "lowest" = install without requesting admin (no UAC).
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=
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
WelcomeLabel2=This will install [name/ver] on your computer.%n%nAperture Neo is a fast, lightweight WPF image viewer. This package is self-contained and includes the .NET runtime, so no additional software installation is required.%n%nThis installer runs in your user profile (no administrator rights required).%n%nWebView2 Runtime is recommended for the modern UI. If not already present, you will be prompted to install it.

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"
Name: "fileassoc"; Description: "Register as a supported image viewer (adds to 'Open With')"; GroupDescription: "File associations:"
Name: "setdefault"; Description: "Set as the &default image viewer for all supported types"; GroupDescription: "File associations:"

[Files]
; Self-contained publish output. The path is overridden at compile time via:
;   ISCC /dPublishDir="<absolute>" Installer\installer.iss
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\Assets\LICENSE.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\Assets\THIRD-PARTY-NOTICES.txt"; DestDir: "{app}"; Flags: ignoreversion
; HarmonyOS Sans SC font files (6 weights: Thin/Light/Regular/Medium/Bold/Black).
; Packaged inside the installer; copied to the per-user Windows fonts directory.
Source: "{#FontDir}\*.ttf"; DestDir: "{localappdata}\Microsoft\Windows\Fonts"; Flags: ignoreversion

[Icons]
Name: "{userstartmenu}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{userstartmenu}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{userdesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

[Registry]
; File association: ProgId under HKCU\Software\Classes (no admin required).
Root: HKCU; Subkey: "Software\Classes\{#ProgId}"; ValueType: string; ValueName: ""; ValueData: "Aperture Neo Image"; Flags: uninsdeletekey; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\{#ProgId}\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#MyAppExeName},0"; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\{#ProgId}\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: fileassoc

; OpenWithProgids (adds to "Open With" menu for each extension)
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

; "Set as default" — override (default) for each extension so the system
; uses Aperture Neo as the primary handler. Optional and off by default.
Root: HKCU; Subkey: "Software\Classes\.jpg"; ValueType: string; ValueName: ""; ValueData: "{#ProgId}"; Flags: uninsdeletevalue; Tasks: setdefault
Root: HKCU; Subkey: "Software\Classes\.jpeg"; ValueType: string; ValueName: ""; ValueData: "{#ProgId}"; Flags: uninsdeletevalue; Tasks: setdefault
Root: HKCU; Subkey: "Software\Classes\.png"; ValueType: string; ValueName: ""; ValueData: "{#ProgId}"; Flags: uninsdeletevalue; Tasks: setdefault
Root: HKCU; Subkey: "Software\Classes\.bmp"; ValueType: string; ValueName: ""; ValueData: "{#ProgId}"; Flags: uninsdeletevalue; Tasks: setdefault
Root: HKCU; Subkey: "Software\Classes\.gif"; ValueType: string; ValueName: ""; ValueData: "{#ProgId}"; Flags: uninsdeletevalue; Tasks: setdefault
Root: HKCU; Subkey: "Software\Classes\.tiff"; ValueType: string; ValueName: ""; ValueData: "{#ProgId}"; Flags: uninsdeletevalue; Tasks: setdefault
Root: HKCU; Subkey: "Software\Classes\.tif"; ValueType: string; ValueName: ""; ValueData: "{#ProgId}"; Flags: uninsdeletevalue; Tasks: setdefault
Root: HKCU; Subkey: "Software\Classes\.webp"; ValueType: string; ValueName: ""; ValueData: "{#ProgId}"; Flags: uninsdeletevalue; Tasks: setdefault
Root: HKCU; Subkey: "Software\Classes\.heic"; ValueType: string; ValueName: ""; ValueData: "{#ProgId}"; Flags: uninsdeletevalue; Tasks: setdefault
Root: HKCU; Subkey: "Software\Classes\.heif"; ValueType: string; ValueName: ""; ValueData: "{#ProgId}"; Flags: uninsdeletevalue; Tasks: setdefault
Root: HKCU; Subkey: "Software\Classes\.avif"; ValueType: string; ValueName: ""; ValueData: "{#ProgId}"; Flags: uninsdeletevalue; Tasks: setdefault
Root: HKCU; Subkey: "Software\Classes\.ico"; ValueType: string; ValueName: ""; ValueData: "{#ProgId}"; Flags: uninsdeletevalue; Tasks: setdefault

; Per-user font registration under HKCU\Software\Microsoft\Windows NT\CurrentVersion\Fonts.
; Windows automatically loads any .ttf from %LOCALAPPDATA%\Microsoft\Windows\Fonts\
; on the next session when its name appears under this HKCU key.
Root: HKCU; Subkey: "Software\Microsoft\Windows NT\CurrentVersion\Fonts"; ValueType: string; ValueName: "HarmonyOS Sans SC Thin (TrueType)";    ValueData: "HarmonyOS Sans SC Thin.ttf";    Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Microsoft\Windows NT\CurrentVersion\Fonts"; ValueType: string; ValueName: "HarmonyOS Sans SC Light (TrueType)";   ValueData: "HarmonyOS Sans SC Light.ttf";   Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Microsoft\Windows NT\CurrentVersion\Fonts"; ValueType: string; ValueName: "HarmonyOS Sans SC Regular (TrueType)"; ValueData: "HarmonyOS Sans SC Regular.ttf"; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Microsoft\Windows NT\CurrentVersion\Fonts"; ValueType: string; ValueName: "HarmonyOS Sans SC Medium (TrueType)";  ValueData: "HarmonyOS Sans SC Medium.ttf";  Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Microsoft\Windows NT\CurrentVersion\Fonts"; ValueType: string; ValueName: "HarmonyOS Sans SC Bold (TrueType)";    ValueData: "HarmonyOS Sans SC Bold.ttf";    Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Microsoft\Windows NT\CurrentVersion\Fonts"; ValueType: string; ValueName: "HarmonyOS Sans SC Black (TrueType)";   ValueData: "HarmonyOS Sans SC Black.ttf";   Flags: uninsdeletevalue

[Code]
const
  WebView2Guid = '{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';
  AppIdGuid    = '{1B6E2D4A-3C8F-4A2E-9D7B-5E1F2A3B4C6D}';

function IsWebView2Installed(): Boolean;
begin
  Result := RegKeyExists(HKLM, 'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\' + WebView2Guid)
         or RegKeyExists(HKLM, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\' + WebView2Guid)
         or RegKeyExists(HKCU, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\' + WebView2Guid);
end;

function NeedsWebView2(): Boolean;
begin
  Result := not IsWebView2Installed();
end;

// Resolve a previous installer (same AppId) and, if present, run its unins000.exe
// silently so this installer can replace it. After confirmation from the user.
// Returns True if the new install should proceed.
function RemovePreviousVersion(): Boolean;
var
  UninstallKey: String;
  UninstallStr: String;
  UninstExe: String;
  ResultCode: Integer;
  Found: Boolean;
begin
  Result := True;
  Found := False;
  UninstallKey := 'Software\Microsoft\Windows\CurrentVersion\Uninstall\' + AppIdGuid + '_is1';

  if RegQueryStringValue(HKLM, UninstallKey, 'UninstallString', UninstallStr) then
    Found := True
  else if RegQueryStringValue(HKCU, UninstallKey, 'UninstallString', UninstallStr) then
    Found := True;

  if not Found then Exit;

  UninstExe := ExtractFilePath(UninstallStr) + 'unins000.exe';

  if MsgBox(
    'A previous version of Aperture Neo was detected on this computer.' + #13#10#13#10 +
    'It will be uninstalled automatically before this new version is installed.' + #13#10 +
    'Your settings, thumbnails and favorites will be preserved by the application itself.' + #13#10#13#10 +
    'Continue?',
    mbConfirmation, MB_YESNO) = IDNO then
  begin
    Result := False;
    Exit;
  end;

  // /SILENT hides the uninstaller's progress UI; /NORESTART suppresses any reboot prompt.
  // ewWaitUntilTerminated blocks until unins000.exe exits, releasing file handles so
  // the new installer can overwrite {app}.
  if not Exec(UninstExe, '/SILENT /NORESTART', '', SW_HIDE,
              ewWaitUntilTerminated, ResultCode) then
  begin
    MsgBox('Failed to launch the previous version''s uninstaller:' + #13#10 +
           UninstExe + #13#10#13#10 +
           'Please remove it manually (Settings -> Apps -> Installed apps) and run this installer again.',
       mbCriticalError, MB_OK);
    Result := False;
  end;
end;

// Show a warning at startup if WebView2 is missing. The app will still install
// (the rest of the app works without it; only the modern FluentWindow chrome
// needs WebView2).
function InitializeSetup(): Boolean;
var
  ErrorCode: Integer;
begin
  if not RemovePreviousVersion() then
  begin
    Result := False;
    Exit;
  end;

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

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    // Font registry entries and font file copies are written by the [Registry]
    // and [Files] sections. The app's MigrateLegacyData() (App.OnStartup)
    // preserves user data when the new build starts.
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  I: Integer;
  Ext: String;
  Extensions: Array of String;
  Fonts: Array of String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    // Clean per-user file association entries written by this installer.
    Extensions := ['.jpg', '.jpeg', '.png', '.bmp', '.gif', '.tiff', '.tif', '.webp', '.heic', '.heif', '.avif', '.ico'];
    for I := 0 to GetArrayLength(Extensions) - 1 do
    begin
      Ext := Extensions[I];
      RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Classes\' + Ext + '\OpenWithProgids\{#ProgId}');
      // Only remove the (default) override we wrote; leave any user-installed default alone if absent.
      if RegValueExists(HKCU, 'Software\Classes\' + Ext, '') then
      begin
        // Use RegDeleteValue-if-matches; Inno Setup only has RegDeleteValue.
        // The (default) value is removed via the uninsdeletevalue flag on the [Registry] entry.
      end;
    end;
    RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Classes\{#ProgId}');

    // Remove the per-user font registration values. The .ttf files in
    // %LOCALAPPDATA%\Microsoft\Windows\Fonts\ are left in place so other apps
    // that may also use HarmonyOS Sans SC keep working.
    Fonts := [
      'HarmonyOS Sans SC Thin (TrueType)',
      'HarmonyOS Sans SC Light (TrueType)',
      'HarmonyOS Sans SC Regular (TrueType)',
      'HarmonyOS Sans SC Medium (TrueType)',
      'HarmonyOS Sans SC Bold (TrueType)',
      'HarmonyOS Sans SC Black (TrueType)'
    ];
    for I := 0 to GetArrayLength(Fonts) - 1 do
      RegDeleteValue(HKCU, 'Software\Microsoft\Windows NT\CurrentVersion\Fonts', Fonts[I]);
  end;
end;
