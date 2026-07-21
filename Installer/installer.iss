; Aperture Neo Inno Setup Script
; Requires Inno Setup 6.0 or later
; Compiled with: ISCC /dMyAppVersion=x.y.z /dPublishFdDir="<abs>" Installer\installer.iss
;
; Bilingual installer (English + 简体中文). Inno Setup shows a
; language selection dialog at startup when 2+ languages are
; listed in the [Languages] section; the chosen language is
; then used for every [Messages], [Tasks] description, and
; [Code] CustomMessage lookup. English is the default (first
; in the list); Chinese(simplified) is the second language.
;
; Per-user, no-admin installer:
;   - PrivilegesRequired=user
;   - Installs to {localappdata}\Programs\ApertureNeo
;   - All registry keys under HKCU
;   - Detects an existing installation of the same AppId and silently
;     uninstalls it (after user confirmation) before installing the new
;     version. Settings/cache are preserved by the app's MigrateLegacyData().
;   - Kills any running ApertureNeo.exe before uninstalling the old version.
;
; v4.0.0 features:
;   - Per-user PATH integration: appends the install directory to
;     HKCU\Environment\Path (REG_EXPAND_SZ) so `aperture ocr ...`
;     is invokable from any terminal. Toggled via the addtopath
;     task, default checked. Removed on uninstall.
;   - Fonts are bundled in-app (Fonts/), no system-level font
;     registration needed.

#define MyAppName "Aperture Neo"
#define MyAppPublisher "DuJunxi1993"
#define MyAppExeName "ApertureNeo.exe"
#define MyAppURL "https://github.com/DuJunxi1993/Aperture-Neo"

; .NET 10 Desktop Runtime — bundled in the installer and auto-installed
; if the system doesn't have it yet. Downloaded from:
;   https://builds.dotnet.microsoft.com/dotnet/WindowsDesktop/10.0.10/windowsdesktop-runtime-10.0.10-win-x64.exe
#define DotNetRuntimeDir "DotNetRuntime"
#define DotNetRuntimeExe "windowsdesktop-runtime-10.0.10-win-x64.exe"

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
; English first (international default), Chinese(simplified) second.
; Inno Setup automatically shows the language selection dialog at
; startup when 2+ languages are listed here; the user's choice is
; then used for every [Messages] / [Tasks] / [CustomMessages] lookup.
;
; The Chinese (Simplified) language file is not bundled with the
; standard Inno Setup install (the official translation lives at
; https://jrsoftware.org/files/istrans/), so we ship a copy in
; Languages/ChineseSimplified.isl and reference it via a relative
; path. Without this, ISCC would fail at compile time with
; "Couldn't open include file compiler:Languages\Chinese.isl".
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "chinesesimp"; MessagesFile: "Languages\ChineseSimplified.isl"

[Messages]
; Default (English) WelcomeLabel2. The Chinese override below
; takes effect when the user picks 简体中文 at the language dialog.
WelcomeLabel2=This will install [name/ver] on your computer.%n%nAperture Neo is a fast, lightweight WPF image viewer. This installer will detect whether .NET 10 Desktop Runtime is already installed and install it automatically if needed.%n%nThis installer runs in your user profile (no administrator rights required). Only .NET Runtime installation will request administrator privileges.%n%nWebView2 Runtime is recommended for the modern UI. If not already present, you will be prompted to install it.
WelcomeLabel2=即将在您的电脑上安装 [name/ver]。%n%nAperture Neo 是一款快速、轻量级的 WPF 图片查看器。本安装程序会自动检测 .NET 10 桌面运行时是否已安装,并会在需要时自动安装。%n%n本安装器在您的用户配置目录下运行,无需管理员权限。仅 .NET 运行时安装需要管理员权限。%n%n建议安装 WebView2 运行时以获得完整的现代化界面。如未安装,稍后会提示您下载。; Languages: chinesesimp

[Tasks]
; Each task declares itself ONCE with {cm:KeyName} references for
; Description and GroupDescription. The actual strings live in
; [CustomMessages] with English as the default and a
; `; Languages: chinesesimp` override. The {cm:...} lookup is
; resolved at runtime based on the active language, so the
; user sees exactly one entry per task in either language.
;
; P2 fix: the previous design duplicated each task (one entry
; without a `Languages:` qualifier, one with `Languages:
; chinesesimp`). Inno Setup treats those as two separate tasks
; — the English-default one is created for ALL languages
; (including Chinese), so when the user picked 简体中文 they
; saw BOTH the English and the Chinese entry as duplicates.
; The {cm:...} indirection is the only correct way to do
; bilingual tasks in Inno Setup.
Name: "desktopicon"; Description: "{cm:Task_DesktopIcon_Description}"; GroupDescription: "{cm:Task_Group_Shortcuts}"

; v3.1.0: per-user PATH integration. Adds the install directory
; to HKCU\Environment\Path (REG_EXPAND_SZ) so `aperture ocr ...`
; is invokable from any terminal. Windows caches environment
; variables at process start — already-running terminals need
; a restart to see the change. Removed on uninstall. Default
; checked via the Check: AddToPathShouldBeChecked helper.
Name: "addtopath"; Description: "{cm:Task_AddToPath_Description}"; GroupDescription: "{cm:Task_Group_Shell}"; Check: AddToPathShouldBeChecked

; v4.0.1: per-user file associations for supported image formats.
; Registers ApertureNeo.Image ProgID under HKCU\Software\Classes
; and points .jpg/.png/… at it. Removed on uninstall.
Name: "assocfiles"; Description: "{cm:Task_AssocFiles_Description}"; GroupDescription: "{cm:Task_Group_Shell}"; Check: AssocFilesShouldBeChecked

; v4.0.5: optional cleanup of old per-user app data (thumbnails,
; settings, recent files). Useful for fresh-start scenarios when
; migrating to a new machine or troubleshooting launch issues.
; Default unchecked — the user must explicitly opt in.
Name: "cleandata"; Description: "{cm:Task_CleanData_Description}"; GroupDescription: "{cm:Task_Group_Shell}"

[CustomMessages]
; All custom messages are defined here in one place. English entries
; without a `; Languages:` qualifier are the default; the
; `; Languages: chinesesimp` overrides apply when the user picks
; 简体中文 at the installer's language dialog. %1, %2, ... in a
; message are replaced with positional parameters passed to
; CustomMessage('KeyName', Param1, Param2, ...).
;
; The first block is consumed by the [Code] section's MsgBox
; prompts. The second block is consumed by {cm:...} lookups in
; [Tasks] / [Run] / etc. — putting every translatable string in
; [CustomMessages] keeps the bilingual indirection uniform
; (every {cm:...} is resolved at runtime against this single
; table, with the same default + override pattern).

; --- [Code] MsgBox prompts (used by MsgBox(CustomMessage(...))) ---

PreviousVersionPrompt=A previous version of Aperture Neo was detected on this computer.%n%nIt will be uninstalled automatically before this new version is installed.%n%nYour settings, thumbnails and favorites will be preserved by the application itself.%n%nContinue?
UninstallFailedMsg=Failed to launch the previous version's uninstaller:%n%1%n%nPlease remove it manually (Settings -> Apps -> Installed apps) and run this installer again.
WebView2Prompt=WebView2 Runtime was not detected on this system.%n%nAperture Neo can still be installed, but the modern UI (Mica / FluentWindow) requires WebView2. The classic WPF chrome will be used as a fallback.%n%nDownload WebView2 Evergreen Bootstrapper now?%n%n(You can also install it later from:%nhttps://developer.microsoft.com/microsoft-edge/webview2/)

PreviousVersionPrompt=检测到您的电脑已安装了旧版 Aperture Neo。%n%n安装新版之前,系统将自动卸载旧版。%n%n您的设置、缩略图和收藏夹由应用本身保留。%n%n是否继续?; Languages: chinesesimp
UninstallFailedMsg=无法启动旧版的卸载程序:%n%1%n%n请手动卸载旧版(设置 → 应用 → 已安装的应用),然后再次运行本安装程序。; Languages: chinesesimp
WebView2Prompt=未在系统中检测到 WebView2 运行时。%n%n您仍可安装 Aperture Neo,但现代化界面(Mica / FluentWindow)需要 WebView2,回退到经典 WPF 界面。%n%n立即下载 WebView2 Evergreen Bootstrapper?%n%n(也可稍后从以下地址下载安装:%nhttps://developer.microsoft.com/microsoft-edge/webview2/); Languages: chinesesimp

; --- [Tasks] descriptions + group headings (consumed by {cm:...}
;     lookups in [Tasks]; see that section for the indirection
;     pattern). One pair per task — keeping the text centralized
;     here avoids the previous design's bug where each [Tasks]
;     entry was duplicated (one with English, one with
;     `Languages: chinesesimp`), which Inno Setup rendered as
;     two separate checkboxes. ---

Task_DesktopIcon_Description=Create a &desktop shortcut
Task_DesktopIcon_Description=创建桌面快捷方式(&D); Languages: chinesesimp
Task_Group_Shortcuts=Shortcuts:
Task_Group_Shortcuts=快捷方式:; Languages: chinesesimp

Task_AddToPath_Description=Add install dir to &PATH (use 'aperture' command from any terminal)
Task_AddToPath_Description=将安装目录添加到 &PATH(任意终端可用 `aperture` 命令); Languages: chinesesimp

Task_AssocFiles_Description=Register &Aperture Neo as the default image viewer
Task_AssocFiles_Description=将 Aperture Neo 注册为默认图片查看器(&A); Languages: chinesesimp

Task_CleanData_Description=Clear old &app data (thumbnails, settings, recent files)
Task_CleanData_Description=清除旧的应用程序数据(&A)(缩略图、设置、最近文件); Languages: chinesesimp

Task_Group_Shell=Shell integration:
Task_Group_Shell=系统集成:; Languages: chinesesimp

; --- [Run] "Launch" checkbox on the final wizard page ---

Run_Launch_Description=Launch {#MyAppName}
Run_Launch_Description=启动 {#MyAppName}; Languages: chinesesimp

[Files]
; Framework-dependent publish output. Requires .NET 10 Desktop Runtime
; on the target machine — the installer auto-installs it if missing.
; The path is overridden at compile time via:
;   ISCC /dPublishFdDir="<absolute>" Installer/installer.iss
Source: "{#PublishFdDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\Assets\LICENSE.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\Assets\THIRD-PARTY-NOTICES.txt"; DestDir: "{app}"; Flags: ignoreversion
; notify.ps1 — PowerShell helper that shows a Win10/11 toast via
; Windows.UI.Notifications (WinRT). Invoked by Cli/OcrCommand.cs
; after headless OCR completes, to give the user feedback
; without spawning a window. The OcrQuick verb (SystemFileAssociations\
; image\shell\ocr-quick) routes the verb's command through
; ApertureNeo.exe ocr (no `-g`) which copies to the clipboard
; and then calls this script.
Source: "notify.ps1"; DestDir: "{app}"; Flags: ignoreversion
; .NET 10 Desktop Runtime offline installer — extracted to {tmp} and
; launched with /quiet if the system doesn't have it yet.
; See https://dotnet.microsoft.com/en-us/download/dotnet/10.0
Source: "{#DotNetRuntimeDir}\{#DotNetRuntimeExe}"; Flags: dontcopy

[Icons]
Name: "{userstartmenu}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{userstartmenu}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{userdesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:Run_Launch_Description}"; Flags: nowait postinstall skipifsilent

[Registry]
; v4.0.1: per-user file associations. Registers the ProgID and
; sets each supported image extension to open with Aperture Neo.
; All keys are removed on uninstall (uninsdeletekey for ProgID,
; uninsdeletevalue for extension defaults). Tasks: assocfiles so
; the user can opt out.
Root: HKCU; Subkey: "Software\Classes\ApertureNeo.Image"; ValueType: string; ValueName: ""; ValueData: "Aperture Neo Image"; Flags: uninsdeletekey; Tasks: assocfiles
Root: HKCU; Subkey: "Software\Classes\ApertureNeo.Image\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\ApertureNeo.exe,0"; Tasks: assocfiles
Root: HKCU; Subkey: "Software\Classes\ApertureNeo.Image\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\ApertureNeo.exe"" ""%1"""; Tasks: assocfiles
Root: HKCU; Subkey: "Software\Classes\.jpg"; ValueType: string; ValueName: ""; ValueData: "ApertureNeo.Image"; Flags: uninsdeletevalue; Tasks: assocfiles
Root: HKCU; Subkey: "Software\Classes\.jpeg"; ValueType: string; ValueName: ""; ValueData: "ApertureNeo.Image"; Flags: uninsdeletevalue; Tasks: assocfiles
Root: HKCU; Subkey: "Software\Classes\.png"; ValueType: string; ValueName: ""; ValueData: "ApertureNeo.Image"; Flags: uninsdeletevalue; Tasks: assocfiles
Root: HKCU; Subkey: "Software\Classes\.bmp"; ValueType: string; ValueName: ""; ValueData: "ApertureNeo.Image"; Flags: uninsdeletevalue; Tasks: assocfiles
Root: HKCU; Subkey: "Software\Classes\.gif"; ValueType: string; ValueName: ""; ValueData: "ApertureNeo.Image"; Flags: uninsdeletevalue; Tasks: assocfiles
Root: HKCU; Subkey: "Software\Classes\.tiff"; ValueType: string; ValueName: ""; ValueData: "ApertureNeo.Image"; Flags: uninsdeletevalue; Tasks: assocfiles
Root: HKCU; Subkey: "Software\Classes\.tif"; ValueType: string; ValueName: ""; ValueData: "ApertureNeo.Image"; Flags: uninsdeletevalue; Tasks: assocfiles
Root: HKCU; Subkey: "Software\Classes\.webp"; ValueType: string; ValueName: ""; ValueData: "ApertureNeo.Image"; Flags: uninsdeletevalue; Tasks: assocfiles
Root: HKCU; Subkey: "Software\Classes\.heic"; ValueType: string; ValueName: ""; ValueData: "ApertureNeo.Image"; Flags: uninsdeletevalue; Tasks: assocfiles
Root: HKCU; Subkey: "Software\Classes\.heif"; ValueType: string; ValueName: ""; ValueData: "ApertureNeo.Image"; Flags: uninsdeletevalue; Tasks: assocfiles
Root: HKCU; Subkey: "Software\Classes\.avif"; ValueType: string; ValueName: ""; ValueData: "ApertureNeo.Image"; Flags: uninsdeletevalue; Tasks: assocfiles

[Code]
const
  WebView2Guid = '{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';
  AppIdGuid    = '{1B6E2D4A-3C8F-4A2E-9D7B-5E1F2A3B4C6D}';
  PathEnvVar   = 'Path';

// Minimal SplitString implementation — ISCC 6.7's `uses
// StrUtils;` clause hit a parser bug ("Unknown identifier ''"
// at column 6 of the `uses` line) and we don't actually need
// anything else from the unit. Allocates a TArrayOfString
// and returns the elements between the delimiter. Empty
// entries (consecutive delimiters) are skipped, matching
// StringSplitOptions.RemoveEmptyEntries semantics.
function SplitPath(const S, Delimiter: string): TArrayOfString;
var
  Chars: TArrayOfString;
  I, Count, Start, DLen, SLen: Integer;
begin
  SetLength(Result, 0);
  if S = '' then Exit;
  SLen := Length(S);
  DLen := Length(Delimiter);
  if DLen = 0 then Exit;
  SetLength(Chars, SLen);
  Count := 0;
  Start := 1;
  for I := 1 to SLen do
  begin
    if Copy(S, I, DLen) = Delimiter then
    begin
      if I > Start then
      begin
        Chars[Count] := Copy(S, Start, I - Start);
        Count := Count + 1;
      end;
      Start := I + DLen;
      I := Start - 1;  // skip the delimiter chars
    end;
  end;
  if Start <= SLen then
  begin
    Chars[Count] := Copy(S, Start, SLen - Start + 1);
    Count := Count + 1;
  end;
  SetLength(Result, Count);
  for I := 0 to Count - 1 do
    Result[I] := Chars[I];
end;

// v3.1.0: per-user PATH integration helpers. Read / write
// HKCU\Environment (REG_EXPAND_SZ so %USERPROFILE% etc. keep
// working) and modify the Path value to add or remove a single
// entry. No admin required — purely per-user. The Path value is
// semicolon-separated on Windows; comparisons are case-insensitive
// (CompareText) to match the OS's case-insensitive path handling.
//
// Uses the high-level Inno Setup registry API (RegQueryStringValue
// / RegWriteExpandStringValue) instead of RegOpenKeyEx so the
// [Code] section doesn't need `uses Windows;` — ISCC 6.7's
// `uses` parser intermittently rejects the otherwise-valid form.

function GetEnvVarValue(const EnvVarName: string; var EnvVarValue: string): Boolean;
begin
  Result := RegQueryStringValue(HKEY_CURRENT_USER, 'Environment', EnvVarName, EnvVarValue);
end;

function SetEnvVarValue(const EnvVarName, EnvVarValue: string): Boolean;
begin
  // Must use the EXPAND variant: HKCU\Environment\Path is
  // REG_EXPAND_SZ (Windows evaluates %USERPROFILE% etc. on
  // read). RegWriteStringValue would write a plain string and
  // break any %-prefixed entries already in the user's PATH.
  Result := RegWriteExpandStringValue(HKEY_CURRENT_USER, 'Environment', EnvVarName, EnvVarValue);
  // Note: we don't broadcast WM_SETTINGCHANGE because the
  // SendMessage call requires `uses Windows;` which has parser
  // issues in ISCC 6.7. New processes (e.g. newly opened
  // terminals) will see the new PATH anyway; already-running
  // terminals need to be restarted.
end;

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

// v3.1.0: Default-checked helpers for the [Tasks] entries. Inno Setup's
// [Tasks] `Flags: checked` parameter triggers an ISCC 6.7.1 parser
// bug ("Parameter 'Flags' includes an unknown flag") that we can't
function AddToPathShouldBeChecked(): Boolean;
begin
  Result := True;
end;

function AssocFilesShouldBeChecked(): Boolean;
begin
  Result := True;
end;

// Resolve a previous installer (same AppId) and, if present, run its
// unins000.exe silently so this installer can replace it. After
// confirmation from the user. Returns True if the new install
// should proceed.
//
// Resilient to a stale registry entry whose uninstaller file has
// been deleted (typical after a user manually cleaned up, or after
// a previous install's uninstaller was quarantined). In that case
// the new install's [Files] `ignoreversion` flag overwrites the app
// files, the [Registry] section rewrites the entries, and Inno
// Setup generates a fresh unins000.exe on install completion — so
// we just skip the silent uninstall and let the new install
// proceed rather than refusing with a critical error.
// Kill any running ApertureNeo.exe so file handles are released
// before the uninstaller runs. Uses taskkill.exe (bundled with
// Windows since XP). If the process isn't running, taskkill exits
// with a non-zero code, which we ignore. Failures are logged but
// non-fatal.
procedure KillAppProcess();
var
  ResultCode: Integer;
begin
  Exec('taskkill.exe', '/F /IM ApertureNeo.exe /T', '', SW_HIDE,
       ewWaitUntilTerminated, ResultCode);
  if ResultCode = 0 then
    Log('Killed running ApertureNeo.exe processes.')
  else
    Log('taskkill.exe returned ' + IntToStr(ResultCode) +
        ' (process was likely not running).');
end;

function RemovePreviousVersion(): Boolean;
var
  UninstallKey: String;
  UninstallStr: String;
  UninstDir: String;
  UninstExe: String;
  ResultCode: Integer;
  Found: Boolean;
  FileFound: Boolean;
begin
  Result := True;
  Found := False;
  UninstallKey := 'Software\Microsoft\Windows\CurrentVersion\Uninstall\' + AppIdGuid + '_is1';

  if RegQueryStringValue(HKLM, UninstallKey, 'UninstallString', UninstallStr) then
    Found := True
  else if RegQueryStringValue(HKCU, UninstallKey, 'UninstallString', UninstallStr) then
    Found := True;

  if not Found then Exit;

  // Registry values may be quoted when the path contains spaces —
  // ExtractFilePath doesn't strip quotes, so "C:\path\unins000.exe"
  // would become "C:\path\unins000.exe\" with a stray trailing
  // quote. Strip a single leading/trailing pair if present.
  if (UninstallStr <> '') and (UninstallStr[1] = '"') then
    UninstallStr := Copy(UninstallStr, 2, Length(UninstallStr) - 2);
  UninstDir := ExtractFilePath(UninstallStr);

  // Kill any running instance of the app so file handles are free.
  KillAppProcess();

  // Probe the standard Inno Setup uninstaller names. unins000.exe
  // is the default; unins001/002 appear if ISCC is recompiled into
  // the same output directory multiple times. We try them in order
  // and bail silently if none exist — see the function-level comment
  // for why this is safe (the new install overwrites the rest).
  UninstExe := '';
  FileFound := False;
  if FileExists(UninstDir + 'unins000.exe') then
  begin
    UninstExe := UninstDir + 'unins000.exe';
    FileFound := True;
  end
  else if FileExists(UninstDir + 'unins001.exe') then
  begin
    UninstExe := UninstDir + 'unins001.exe';
    FileFound := True;
  end
  else if FileExists(UninstDir + 'unins002.exe') then
  begin
    UninstExe := UninstDir + 'unins002.exe';
    FileFound := True;
  end;

  if not FileFound then
  begin
    // Stale registry entry: the previous install's uninstaller file
    // is gone, but the UninstallString still points at its former
    // path. Log to the installer's debug log for diagnostics and
    // proceed — the new install will rewrite the registry and Inno
    // Setup will create a fresh unins000.exe at the end of install.
    Log('Previous version uninstaller not found at ' + UninstDir +
        ' (unins000/001/002.exe all missing); skipping uninstall.');
    Exit;
  end;

  if MsgBox(
    CustomMessage('PreviousVersionPrompt'),
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
    // Uninstaller couldn't even start — warn but let the user
    // continue. The new install's [Files] `ignoreversion` flag will
    // overwrite the old files anyway, and Inno Setup generates a
    // fresh unins000.exe at the end of the install.
    if MsgBox(
      FmtMessage(CustomMessage('UninstallFailedMsg'), [UninstExe]),
      mbError, MB_YESNO) = IDNO then
      Result := False;
  end
  else if ResultCode <> 0 then
  begin
    // Uninstaller ran but returned an error (e.g. some files could not
    // be removed). Warn but allow the install to proceed — the new
    // files will overwrite whatever remains.
    Log('Previous version uninstaller exited with code ' + IntToStr(ResultCode) +
        '; continuing with new install.');
  end;
end;

// v3.1.0: per-user PATH integration helpers. Read / write HKCU\Environment
// (REG_EXPAND_SZ so %USERPROFILE% etc. keep working) and modify the Path
// value to add or remove a single entry. No admin required — purely
// per-user. The Path value is semicolon-separated on Windows; comparisons
// are case-insensitive (CompareText) to match the OS's case-insensitive
// path handling.
//
// GetEnvVarValue and SetEnvVarValue are defined above (near the
// AppIdGuid constant block) to keep all PATH-related code together
// in source order — the earlier definition uses LongWord instead of
// HKEY (the latter isn't in the standard Inno Setup types and the
// compiler rejects the declaration). See the comment block there.

function PathEntryExists(const PathValue, NewEntry: string): Boolean;
var
  Entries: TArrayOfString;
  I: Integer;
begin
  Result := False;
  if PathValue = '' then Exit;
  Entries := SplitPath(PathValue, ';');
  for I := 0 to GetArrayLength(Entries) - 1 do
    if CompareText(Trim(Entries[I]), Trim(NewEntry)) = 0 then
    begin
      Result := True;
      Exit;
    end;
end;

function AddPathEntry(const NewEntry: string): Boolean;
var
  CurrentPath, NewPath: string;
begin
  Result := False;
  if not GetEnvVarValue(PathEnvVar, CurrentPath) then
    CurrentPath := '';
  if PathEntryExists(CurrentPath, NewEntry) then
  begin
    // Idempotent: already present, treat as success.
    Result := True;
    Exit;
  end;
  if CurrentPath = '' then
    NewPath := NewEntry
  else
    NewPath := CurrentPath + ';' + NewEntry;
  Result := SetEnvVarValue(PathEnvVar, NewPath);
end;

function RemovePathEntry(const OldEntry: string): Boolean;
var
  CurrentPath, NewPath: string;
  Entries: TArrayOfString;
  I: Integer;
begin
  Result := False;
  if not GetEnvVarValue(PathEnvVar, CurrentPath) then Exit;
  if not PathEntryExists(CurrentPath, OldEntry) then
  begin
    // Idempotent: not present, treat as success.
    Result := True;
    Exit;
  end;
  Entries := SplitPath(CurrentPath, ';');
  NewPath := '';
  for I := 0 to GetArrayLength(Entries) - 1 do
  begin
    if CompareText(Trim(Entries[I]), Trim(OldEntry)) = 0 then Continue;
    if NewPath = '' then
      NewPath := Trim(Entries[I])
    else
      NewPath := NewPath + ';' + Trim(Entries[I]);
  end;
  Result := SetEnvVarValue(PathEnvVar, NewPath);
end;

// Detect whether .NET 10 Desktop Runtime is installed by checking the
// registry under Microsoft.WindowsDesktop.App for any version starting
// with "10.". We check both 64-bit and 32-bit views (though this
// installer targets x64, the runtime may be registered in either hive).
// Returns True if at least one 10.x runtime is found.
function IsDotNet10DesktopInstalled(): Boolean;
var
  Names: TArrayOfString;
  I: Integer;
begin
  Result := False;

  // Check 64-bit view first
  if RegGetSubkeyNames(HKLM64,
    'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App',
    Names) then
  begin
    for I := 0 to GetArrayLength(Names) - 1 do
    begin
      if Copy(Names[I], 1, 3) = '10.' then
      begin
        Log('.NET 10 Desktop Runtime found (x64): ' + Names[I]);
        Result := True;
        Exit;
      end;
    end;
  end;

  // Fall back to 32-bit view
  if RegGetSubkeyNames(HKLM32,
    'SOFTWARE\dotnet\Setup\InstalledVersions\x86\sharedfx\Microsoft.WindowsDesktop.App',
    Names) then
  begin
    for I := 0 to GetArrayLength(Names) - 1 do
    begin
      if Copy(Names[I], 1, 3) = '10.' then
      begin
        Log('.NET 10 Desktop Runtime found (x86): ' + Names[I]);
        Result := True;
        Exit;
      end;
    end;
  end;

  Log('.NET 10 Desktop Runtime is NOT installed.');
end;

// Run the bundled .NET 10 Desktop Runtime installer with administrator
// privileges (ShellExec 'runas'). The installer is extracted to {tmp}
// by Inno Setup's [Files] system. Uses /quiet + /norestart for silent
// install; the user will see the UAC prompt but no further interaction
// is needed. Returns True if the installer completed successfully.
function InstallDotNetRuntime(): Boolean;
var
  ResultCode: Integer;
  RuntimeExe: string;
begin
  // Extract the bundled .NET runtime installer from the setup to {tmp}.
  // This runs inside InitializeSetup, before the main [Files] phase,
  // so we use ExtractTemporaryFile (with Flags: dontcopy) instead of
  // relying on DestDir.
  ExtractTemporaryFile('{#DotNetRuntimeExe}');
  RuntimeExe := ExpandConstant('{tmp}\{#DotNetRuntimeExe}');
  Log('Launching .NET Runtime installer: ' + RuntimeExe);

  if not FileExists(RuntimeExe) then
  begin
    MsgBox('Failed to locate .NET Runtime installer.', mbError, MB_OK);
    Result := False;
    Exit;
  end;

  // 'runas' = run with administrator privileges (triggers UAC).
  // /install /quiet /norestart = silent install, no reboot.
  if ShellExec('runas', RuntimeExe, '/install /quiet /norestart', '',
    SW_SHOWNORMAL, ewWaitUntilTerminated, ResultCode) then
  begin
    if ResultCode = 0 then
    begin
      Log('.NET Runtime installer completed successfully.');
      Result := True;
    end
    else
    begin
      Log('.NET Runtime installer exited with code ' + IntToStr(ResultCode));
      MsgBox('.NET Runtime installation failed (error code: ' +
        IntToStr(ResultCode) + ').' + #13#10 +
        'Please install it manually from:' + #13#10 +
        'https://dotnet.microsoft.com/en-us/download/dotnet/10.0',
        mbError, MB_OK);
      Result := False;
    end;
  end
  else
  begin
    Log('Failed to launch .NET Runtime installer.');
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

  // Check .NET 10 Desktop Runtime — install it if missing.
  if not IsDotNet10DesktopInstalled() then
  begin
    if MsgBox(
      '.NET 10 Desktop Runtime is required but not found on this system.' + #13#10#13#10 +
      'This installer includes the .NET Runtime (approx. 57 MB). ' +
      'Would you like to install it now?' + #13#10#13#10 +
      'Administrator permission is required for .NET Runtime installation.',
      mbConfirmation, MB_YESNO) = IDYES then
    begin
      if not InstallDotNetRuntime() then
      begin
        MsgBox('.NET Runtime installation failed or was cancelled.' + #13#10 +
          'Setup cannot continue.',
          mbError, MB_OK);
        Result := False;
        Exit;
      end;
    end
    else
    begin
      MsgBox('.NET 10 Desktop Runtime is required to run Aperture Neo.' + #13#10 +
        'Setup will now exit.',
        mbError, MB_OK);
      Result := False;
      Exit;
    end;
  end;

  if NeedsWebView2() then
  begin
    if MsgBox(
      CustomMessage('WebView2Prompt'),
      mbConfirmation, MB_YESNO) = IDYES then
    begin
      ShellExec('open', 'https:' + '/' + '/go.microsoft.com/fwlink/p/?LinkId=2124703', '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
    end;
  end;
  Result := True;
end;

// v4.0.5: delete old per-user app data (thumbnails, settings,
// recent files). Runs before the new files are installed. The
// thumbnail cache sits in %TEMP%\ApertureNeo\thumbs\cache.db;
// settings + recents live under %APPDATA%\ApertureNeo\. Both
// are recreated by the app on next launch.
procedure CleanOldUserData();
var
  TempDir: string;
  AppDataDir: string;
begin
  Log('Cleaning old per-user app data (task: cleandata)...');

  // Resolve %TEMP% via environment variable rather than {tmp}
  // (which points inside an is-xxxxx.tmp subfolder).
  TempDir := GetEnv('TEMP');
  if TempDir <> '' then
  begin
    TempDir := AddBackslash(TempDir) + 'ApertureNeo';
    Log('Deleting thumbnail cache: ' + TempDir);
    DelTree(TempDir, True, True, True);
  end;

  // Resolve %APPDATA% via {userappdata} constant.
  AppDataDir := ExpandConstant('{userappdata}\ApertureNeo');
  Log('Deleting app data: ' + AppDataDir);
  DelTree(AppDataDir, True, True, True);

  Log('Old per-user app data cleared.');
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
  begin
    // v4.0.5: clear old app data if the user checked the
    // `cleandata` task. Runs before the new files are written
    // to {app}, so the old EXE is still running-space safe
    // (the KillAppProcess in RemovePreviousVersion already
    // terminated it).
    if WizardIsTaskSelected('cleandata') then
      CleanOldUserData();
  end;

  if CurStep = ssPostInstall then
  begin
    // v3.1.0: per-user PATH integration. Only runs if the user
    // checked the `addtopath` task at install time. Idempotent —
    // AddPathEntry is a no-op when the path is already in PATH.
    if WizardIsTaskSelected('addtopath') then
      AddPathEntry(ExpandConstant('{app}'));
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
    if CurUninstallStep = usPostUninstall then
    begin
        // v3.1.0: clean per-user PATH entry. Idempotent — no-op if
        // the path wasn't in PATH (or if the addtopath task wasn't
        // checked at install time). Runs unconditionally on
        // uninstall so we don't need to track whether the task was
        // selected.
        RemovePathEntry(ExpandConstant('{app}'));
    end;
end;
