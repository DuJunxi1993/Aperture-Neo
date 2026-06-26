; Aperture Neo Inno Setup Script
; Requires Inno Setup 6.0 or later
; Compiled with: ISCC /dMyAppVersion=x.y.z /dPublishDir="<abs>" Installer\installer.iss
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
;   - Fonts registered to %LOCALAPPDATA%\Microsoft\Windows\Fonts\ + HKCU\...\Fonts
;   - Detects an existing installation of the same AppId and silently
;     uninstalls it (after user confirmation) before installing the new
;     version. Settings/cache are preserved by the app's MigrateLegacyData().
;
; v3.1.0 features:
;   - Win11 right-click OCR: registers under both the per-extension
;     HKCU\Software\Classes\.<ext>\shell\ocr (legacy + Win11
;     "Show more options") and the SystemFileAssociations\image
;     key (Win11 compact menu attempt).
;   - Per-user PATH integration: appends the install directory to
;     HKCU\Environment\Path (REG_EXPAND_SZ) so `aperture ocr ...`
;     is invokable from any terminal. Toggled via the addtopath
;     task, default checked. Removed on uninstall.

#define MyAppName "Aperture Neo"
#define MyAppPublisher "DuJunxi1993"
#define MyAppExeName "ApertureNeo.exe"
#define MyAppURL "https://github.com/DuJunxi1993/Aperture-Neo"
#define SupportedExtensions ".jpg|.jpeg|.png|.bmp|.gif|.tiff|.tif|.webp|.heic|.heif|.avif|.ico"
#define ProgId "ApertureNeo.Image.1"
#define FontDir "Fonts\HarmonyOS_Sans_SC"
; CLSID of the Win11 IExplorerCommand shell extension. Must
; match the [Guid] in Plugins.Ocr.ShellExt/OcrExplorerCommand.cs
; exactly — the COM class is registered under this CLSID and
; the [Registry] entries below point at the same string.
#define OcrShellExtClsid "{{4E8A2D11-3F19-4F4D-A1C5-19B3C4B6F4A1}"

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
WelcomeLabel2=This will install [name/ver] on your computer.%n%nAperture Neo is a fast, lightweight WPF image viewer. This package is self-contained and includes the .NET runtime, so no additional software installation is required.%n%nThis installer runs in your user profile (no administrator rights required).%n%nWebView2 Runtime is recommended for the modern UI. If not already present, you will be prompted to install it.
WelcomeLabel2=即将在您的电脑上安装 [name/ver]。%n%nAperture Neo 是一款快速、轻量级的 WPF 图片查看器。本安装包为自包含模式,已包含 .NET 运行时,无需额外安装其他组件。%n%n本安装器在您的用户配置目录下运行,无需管理员权限。%n%n建议安装 WebView2 运行时以获得完整的现代化界面。如未安装,稍后会提示您下载。; Languages: chinesesimp

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

Name: "fileassoc"; Description: "{cm:Task_FileAssoc_Description}"; GroupDescription: "{cm:Task_Group_FileAssoc}"

Name: "setdefault"; Description: "{cm:Task_SetDefault_Description}"; GroupDescription: "{cm:Task_Group_FileAssoc}"

; OCR shell verb: each-image-extension registration (legacy menu +
; Win11 "Show more options") PLUS SystemFileAssociations\image
; registration further down (Win11 compact menu attempt). v3.1.0
; defaults to checked via the Check: TrueFunc helper in [Code] —
; the [Tasks] `Flags: checked` form triggers an ISCC 6.7.1
; parser bug ("Parameter 'Flags' includes an unknown flag.")
; that we can't work around at the script level.
Name: "ocrverb"; Description: "{cm:Task_OcrVerb_Description}"; GroupDescription: "{cm:Task_Group_Shell}"; Check: OcrVerbShouldBeChecked

; v3.1.0: per-user PATH integration. Adds the install directory
; to HKCU\Environment\Path (REG_EXPAND_SZ) so `aperture ocr ...`
; is invokable from any terminal. Windows caches environment
; variables at process start — already-running terminals need
; a restart to see the change. Removed on uninstall. Default
; checked via the Check: AddToPathShouldBeChecked helper.
Name: "addtopath"; Description: "{cm:Task_AddToPath_Description}"; GroupDescription: "{cm:Task_Group_Shell}"; Check: AddToPathShouldBeChecked

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

Task_FileAssoc_Description=Register as a supported image viewer (adds to 'Open With')
Task_FileAssoc_Description=注册为受支持的图片查看器(加入「打开方式」菜单); Languages: chinesesimp
Task_Group_FileAssoc=File associations:
Task_Group_FileAssoc=文件关联:; Languages: chinesesimp

Task_SetDefault_Description=Set as the &default image viewer for all supported types
Task_SetDefault_Description=设为所有受支持格式的默认图片查看器(&S); Languages: chinesesimp

Task_OcrVerb_Description=Add 'OCR &文字提取' to the right-click menu (Win11 top-level + legacy)
Task_OcrVerb_Description=添加「OCR &文字提取」到右键菜单(Win11 顶层 + 传统菜单); Languages: chinesesimp

Task_AddToPath_Description=Add install dir to &PATH (use 'aperture' command from any terminal)
Task_AddToPath_Description=将安装目录添加到 &PATH(任意终端可用 `aperture` 命令); Languages: chinesesimp

Task_Group_Shell=Shell integration:
Task_Group_Shell=系统集成:; Languages: chinesesimp

; --- [Run] "Launch" checkbox on the final wizard page ---

Run_Launch_Description=Launch {#MyAppName}
Run_Launch_Description=启动 {#MyAppName}; Languages: chinesesimp

[Files]
; Self-contained publish output. The path is overridden at compile time via:
;   ISCC /dPublishDir="<absolute>" Installer/installer.iss
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\Assets\LICENSE.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\Assets\THIRD-PARTY-NOTICES.txt"; DestDir: "{app}"; Flags: ignoreversion
; HarmonyOS Sans SC font files (6 weights: Thin/Light/Regular/Medium/Bold/Black).
; Packaged inside the installer; copied to the per-user Windows fonts directory.
Source: "{#FontDir}\*.ttf"; DestDir: "{localappdata}\Microsoft\Windows\Fonts"; Flags: ignoreversion
; notify.ps1 — PowerShell helper that shows a Win10/11 toast via
; Windows.UI.Notifications (WinRT). Invoked by Cli/OcrCommand.cs
; after headless OCR completes, to give the user feedback
; without spawning a window. The OcrQuick verb (SystemFileAssociations\
; image\shell\ocr-quick) routes the verb's command through
; ApertureNeo.exe ocr (no `-g`) which copies to the clipboard
; and then calls this script.
Source: "notify.ps1"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{userstartmenu}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{userstartmenu}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{userdesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:Run_Launch_Description}"; Flags: nowait postinstall skipifsilent

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

; v3.1.x: Win11 IExplorerCommand shell extension. Replaces
; the previous per-extension IContextMenu verbs (which only
; appeared under "Show more options" on Win11) and the
; "快速 OCR 到剪贴板" IContextMenu attempt. Win11's compact
; top-level context menu is populated by IExplorerCommand
; implementations, so registering one is the only way to get
; the verb to surface there.
;
; The COM class lives in {app}\Plugins\ApertureNeo.Plugins.Ocr.ShellExt.dll
; (built by publish.ps1 step 2.6) and is registered as an
; in-proc server under HKCR\CLSID\{OcrShellExtClsid}. The
; shell extension DLL is loaded by the .NET COM activator
; (mscoree.dll → hostruntime) from the path the installer
; writes to InprocServer32. The .NET runtime is bundled
; alongside (publish is self-contained), so the OS can find
; it via the standard mscoree activation rules.
;
; At Invoke time, the shell extension extracts the selected
; file paths from IShellItemArray and spawns ApertureNeo.exe
; ocr <files> as a fire-and-forget child process. The CLI's
; headless ocr path (load ONNX, OCR, concatenate, copy to
; clipboard, show Windows toast via notify.ps1) handles the
; actual work — the shell extension itself stays sub-second
; so the right-click menu doesn't lag.
;
; For Win10: the same IExplorerCommand verb shows in the
; classic right-click menu (Explorer falls back to IExplorerCommand
; when no other extension is registered for the file type).
; For Win11: it shows in the top-level (compact) context menu.
;
; Gated by the same `ocrverb` task as the old IContextMenu
; entries so the user opts in / out once for OCR right-click
; integration in general. Removing the task also unregisters
; the COM class and the ExplorerCommand association.

; (1) COM class — the actual server object
Root: HKCU; Subkey: "Software\Classes\CLSID\{#OcrShellExtClsid}"; ValueType: string; ValueName: ""; ValueData: "Aperture Neo OCR Shell Extension"; Flags: uninsdeletekey; Tasks: ocrverb
Root: HKCU; Subkey: "Software\Classes\CLSID\{#OcrShellExtClsid}\InprocServer32"; ValueType: string; ValueName: ""; ValueData: "{app}\Plugins\ApertureNeo.Plugins.Ocr.ShellExt.dll"; Flags: uninsdeletekey; Tasks: ocrverb
; Apartment threading is required for shell extensions that
; receive IShellItemArray (the shell uses STA). Without this
; value the COM activation picks the default (free / both) and
; the shell crashes when it tries to call Invoke.
Root: HKCU; Subkey: "Software\Classes\CLSID\{#OcrShellExtClsid}\InprocServer32"; ValueType: string; ValueName: "ThreadingModel"; ValueData: "Apartment"; Flags: uninsdeletekey; Tasks: ocrverb

; (2) Link the COM class to the image file class. The shell
; looks up IExplorerCommand implementations via
; <extension>\shellex\ExplorerCommand\{<guid>}. We register
; under SystemFileAssociations\image so the verb shows for
; every image file (jpg, png, bmp, etc.) without needing a
; per-extension entry — and so the verb also shows for
; extensions we don't enumerate (heic, avif, raw, etc.) as
; long as Windows classifies them under the "image" system
; file association.
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\image\shellex\ExplorerCommand\{#OcrShellExtClsid}"; ValueType: string; ValueName: ""; ValueData: "ApertureNeo.OcrQuick"; Flags: uninsdeletekey; Tasks: ocrverb

; (3) Win11's "Shell Extensions\Approved" whitelist. Modern
; Windows requires user opt-in for shell extensions — without
; an entry here the verb may be silently disabled. We add the
; CLSID under HKCU (per-user; no admin required) so the user
; only authorizes extensions for their own account. The value
; is a friendly name shown in the Shell Extensions control
; panel for transparency.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Approved"; ValueType: string; ValueName: "{#OcrShellExtClsid}"; ValueData: "ApertureNeo.Plugins.Ocr.ShellExt"; Flags: uninsdeletevalue; Tasks: ocrverb

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
// work around at the script level. The `Check:` parameter is a
// Pascal Script expression evaluated at runtime to determine the
// initial checked state — it doesn't share the same parser bug.
// Both helpers return True unconditionally (the v3.1.0 spec
// requires both OCR and PATH to be default-on). If a future
// release wants to make either opt-in, the helper becomes:
//   function OcrVerbShouldBeChecked: Boolean; begin Result := False; end;
// or more sophisticated (e.g. check whether the user has previously
// installed the OCR plugin via the registry).
function OcrVerbShouldBeChecked(): Boolean;
begin
  Result := True;
end;

function AddToPathShouldBeChecked(): Boolean;
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
    MsgBox(
      FmtMessage(CustomMessage('UninstallFailedMsg'), [UninstExe]),
      mbCriticalError, MB_OK);
    Result := False;
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
      CustomMessage('WebView2Prompt'),
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
    //
    // v3.1.0: per-user PATH integration. Only runs if the user
    // checked the `addtopath` task at install time. Idempotent —
    // AddPathEntry is a no-op when the path is already in PATH.
    if WizardIsTaskSelected('addtopath') then
      AddPathEntry(ExpandConstant('{app}'));
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
      // Clean OCR shell verb (per-extension \shell\ocr subkey).
      // The uninsdeletekey flag handles the parent \shell\ocr key.
      RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Classes\' + Ext + '\shell\ocr');
    end;
    RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Classes\{#ProgId}');

    // v3.1.x: clean the Win11 IExplorerCommand shell extension
    // registration. We delete the COM class subtree (which
    // contains both InprocServer32 and the ThreadingModel) plus
    // the ExplorerCommand association under image\, plus the
    // Shell Extensions\Approved whitelist entry.
    RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Classes\CLSID\{#OcrShellExtClsid}');
    RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Classes\SystemFileAssociations\image\shellex\ExplorerCommand\{#OcrShellExtClsid}');
    // The Approved key is shared by all shell extensions, so we
    // only delete the value (not the key) and only if it matches
    // ours — leaving other extensions' approvals intact.
    if RegValueExists(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Approved', '{#OcrShellExtClsid}') then
        RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Approved', '{#OcrShellExtClsid}');

    // v3.1.0: clean per-user PATH entry. Idempotent — no-op if
    // the path wasn't in PATH (or if the addtopath task wasn't
    // checked at install time). Runs unconditionally on
    // uninstall so we don't need to track whether the task was
    // selected.
    RemovePathEntry(ExpandConstant('{app}'));

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
