# Aperture Neo 3.1.0

## Highlights

- **Bilingual installer** — language selection dialog on first launch (English + 简体中文). Every wizard page, task description, and confirmation prompt is localized.
- **Per-user PATH integration** — the install directory is appended to `HKCU\Environment\Path` so `aperture ocr photo.jpg` is invokable from any terminal without specifying the full path. New task `addtopath`, default checked; removed on uninstall.
- **Quick OCR to clipboard** — new "快速 OCR 到剪贴板" verb bypasses the result window and copies OCR text directly to the clipboard, with a Windows toast notification on completion.
- **Win11 right-click OCR** — "OCR 文字提取" shows in the classic context menu (Win10) or under "Show more options" on Win11. Top-level (compact) menu support was attempted via four IExplorerCommand shell-extension iterations but none succeeded — Win11's modern menu requires MSIX package identity or signed shell extensions, which are beyond the current per-user Win32 installer scope.

## New

- **Quick OCR to clipboard** (`Installer/notify.ps1` + `Cli/OcrCommand.cs`): a headless `aperture ocr <file>` path that runs OCR, copies the result to the clipboard, and fires a Windows toast notification via `Windows.UI.Notifications.ToastNotificationManager`. Useful when the user wants the text without the result window.
- **`ApertureNeo.exe help [command]`** subcommand (`b96bb4d`): rich description for every command, with EXAMPLES / NOTES / EXIT CODES / SUPPORTED FORMATS sections. Mirrors `git help` / `cargo help`. Case-insensitive command lookup, friendly error for unknown commands.
- **CLI output reaches the parent terminal** (`b93a280`): `AttachConsole(ATTACH_PARENT_PROCESS)` in `OnStartup` so WinExe CLI invocations from PowerShell / cmd.exe / Windows Terminal actually print to the terminal instead of the void. No effect on GUI launches from a non-console parent (Explorer double-click etc.).
- **Bilingual installer UI** (`Installer/installer.iss`): `[Languages]` lists `english` first + `chinesesimp` second; `[Messages]`, `[Tasks]`, and `[CustomMessages]` all carry `; Languages: chinesesimp` overrides. Inno Setup automatically shows the language selection dialog at startup.

## Changed

- `ocrverb` task now defaults to checked (was off in v3.0.0). OCR is enabled out of the box.
- `addtopath` task new, default checked.
- `publish.ps1` default version bumped 2.0.0 → 3.1.0.
- OCR right-click verb now also registered under `HKCU\Software\Classes\SystemFileAssociations\image\shell\ocr` so it works for every image file type (not just per-extension).
- Win11's compact (top-level) context menu was investigated via four shell-extension prototypes (manual IExplorerCommand COM interop, deps.json fix, EnableComHosting comhost, ContextMenuHandlers path). None succeeded because Win11 only surfaces IExplorerCommand verbs from apps with MSIX package identity or signed extensions. The IContextMenu fallback (registered under per-extension `shell\ocr` + `SystemFileAssociations\image\shell\ocr`) works on all supported Windows versions — Win10 shows it in the classic menu, Win11 shows it under "Show more options".

## Fixed

- **Folder tree back-navigation** (`d06623c` / `2aeeaf6` / `6b60341`)
  - `Back` from any folder now reloads that folder's images in the thumbnail grid (previously left the drilled-in folder's images showing).
  - `Back` from a leaf in drill mode returns to the drill level (e.g. `/level1/level2/level3` leaf → back → loads `/level1/level2`, not `/level1`).
  - `Return to root` reloads the most-recent folder.
  - `JumpToDirectory` (right-click Recent → "Open in tree") now populates the back-stack with the correct per-level loaded-folder values, so back-stepping through a multi-level jump works.
- **Fullscreen exit auto-maximize** (`a47a5c6` / `832d71a`)
  - Exiting fullscreen no longer auto-maximizes a window that wasn't already maximized when the user pressed Ctrl+F. The two-method `NotifyEnteringFullscreen` + `Toggle` protocol is gone; the pre-toggle capture is now inside `Toggle()` so future call sites can't reintroduce the bug.
- **CLI dispatch hang on `help` subcommand**: the `HelpCommand` previously deadlocked when called from a WPF UI thread context (help writer posted to the dispatcher that was blocked on `InvokeAsync`). Fixed by running the inner `root.Invoke` on a thread-pool thread via `Task.Run`. No effect on visible behavior; unblocks any user who tried `ApertureNeo.exe help`.
- **OCR text-recognition accuracy on overlapping lines**: re-classified input via the bundled direction model before recognition (v3.0.0 only ran classification on top-down text, missing 90°-rotated scans). No new ONNX files — same 3-model bundle.
- **CLI available from any terminal**: the installer now attaches the WinExe's stdout/stderr to the parent console (via kernel32 `AttachConsole`), so `aperture ocr --help` etc. actually prints output in PowerShell/cmd.exe.
- **AssemblyInfo.cs sync**: the root-level hand-written `AssemblyInfo.cs` was still hard-coded to 3.0.0 from the v3.0.0 release, so v3.1.0's published binary had AssemblyVersion=3.0.0.0 despite the csproj saying 3.1.0. This caused WPF's resource loader to throw `FileNotFoundException` at startup (the auto-generated App.g.cs used the csproj version for pack URIs). Fixed by syncing the three version attributes to match.

## Technical

- `OutputType=WinExe` kept (no separate `aperture.exe` shim). PATH integration is `HKCU\Environment\Path` (REG_EXPAND_SZ, case-insensitive duplicate check, idempotent on re-install, removed on uninstall).
- WPF + SkiaSharp 3.119.4 rendering, FluentWindow 3.0.5 (WebView2).
- ONNX Runtime + PaddleOCR v4 models bundled under `Assets\models\paddleocr\`.
- Plugin architecture via ALC isolation — `Plugins\ApertureNeo.Plugins.Ocr.dll` + `ApertureNeo.Plugins.Ocr.Core.dll` + `ApertureNeo.Plugins.Ocr.Ui.dll` + `ApertureNeo.Strings.dll`.

## Files

- `ApertureNeo-Setup-v3.1.0.exe` — self-contained win-x64 installer (~226 MB, .NET 10 self-contained + OCR plugin + ONNX models + WebView2 prompt)
- `RELEASE-NOTES-v3.1.0.md` (this file)
