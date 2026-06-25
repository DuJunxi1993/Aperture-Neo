# Aperture Neo 3.1.0

## Highlights

- **Bilingual installer** — language selection dialog on first launch (English + 简体中文). Every wizard page, task description, and confirmation prompt is localized.
- **Per-user PATH integration** — the install directory is appended to `HKCU\Environment\Path` so `aperture ocr photo.jpg` is invokable from any terminal without specifying the full path. New task `addtopath`, default checked; removed on uninstall.
- **Win11 right-click OCR (top-level menu)** — OCR verb now also registers under `HKCU\Software\Classes\SystemFileAssociations\image\shell\ocr` so it appears in Win11's compact context menu in addition to the per-extension "Show more options" fallback.

## New

- **CLI on PATH** (`ApertureNeo.exe` + `aperture.exe` shim): installer appends `%LOCALAPPDATA%\Programs\ApertureNeo` to the user's PATH. Windows caches environment at process start — already-running terminals need a restart.
- **Win11 top-level OCR menu** (`SystemFileAssociations\image`): the same `OCR 文字提取` command also registered against the generic image association, so right-clicking any image file in Win11 surfaces the verb directly without clicking "Show more options" first. If Win11 still classifies the verb as "heavy" (GUI spawn), it falls back to "Show more options" via the per-extension registration.
- **`ApertureNeo.exe help [command]`** subcommand (`b96bb4d`): rich description for every command, with EXAMPLES / NOTES / EXIT CODES / SUPPORTED FORMATS sections. Mirrors `git help` / `cargo help`. Case-insensitive command lookup, friendly error for unknown commands.
- **CLI output reaches the parent terminal** (`b93a280`): `AttachConsole(ATTACH_PARENT_PROCESS)` in `OnStartup` so WinExe CLI invocations from PowerShell / cmd.exe / Windows Terminal actually print to the terminal instead of the void. No effect on GUI launches from a non-console parent (Explorer double-click etc.).
- **Bilingual installer UI** (`Installer/installer.iss`): `[Languages]` lists `english` first + `chinesesimp` second; `[Messages]`, `[Tasks]`, and `[CustomMessages]` all carry `; Languages: chinesesimp` overrides. Inno Setup automatically shows the language selection dialog at startup.

## Changed

- `ocrverb` task now defaults to checked (was off in v3.0.0). OCR is enabled out of the box.
- `addtopath` task new, default checked.
- `publish.ps1` default version bumped 2.0.0 → 3.1.0.

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

## Technical

- `OutputType=WinExe` kept (no separate `aperture.exe` shim). PATH integration is `HKCU\Environment\Path` (REG_EXPAND_SZ, case-insensitive duplicate check, idempotent on re-install, removed on uninstall).
- WPF + SkiaSharp 3.119.4 rendering, FluentWindow 3.0.5 (WebView2).
- ONNX Runtime + PaddleOCR v4 models bundled under `Assets\models\paddleocr\`.
- Plugin architecture via ALC isolation — `Plugins\ApertureNeo.Plugins.Ocr.dll` + `ApertureNeo.Plugins.Ocr.Core.dll` + `ApertureNeo.Plugins.Ocr.Ui.dll` + `ApertureNeo.Strings.dll`.

## Files

- `ApertureNeo-Setup-v3.1.0.exe` — self-contained win-x64 installer (~150 MB, OCR plugin + models + WebView2 prompt included)
- `RELEASE-NOTES-v3.1.0.md` (this file)
