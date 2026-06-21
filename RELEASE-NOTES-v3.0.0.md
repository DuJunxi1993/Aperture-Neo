# Aperture Neo 3.0.0

## Major new features

**Plugin system** (`Services/PluginLoader.cs`, `Services/IPlugin.cs`)
- New `IPlugin` extension contract: `Name` / `Description` / `Status` / `Activate` / `Deactivate`
- Isolated `AssemblyLoadContext` per plugin — keeps plugin transitive deps (SkiaSharp 3.119, ONNX 1.26, OpenCV 4.13) from clashing with the main app
- Plugins are discovered at startup (DLLs loaded) but NOT activated — heavy resources stay unloaded until the user opts in via the "插件" submenu checkbox
- The "插件" submenu shows a traffic-light status dot per plugin: `Disabled` (灰) / `Enabled` (绿) / `Unavailable` (红)
- Plugin action items (e.g. "提取当前图片文字") live in the image right-click menu, NOT in the 插件 submenu (the submenu is toggle-only)

**OCR plugin (bundled)** (`Plugins.Ocr/`)
- PaddleOCR v4 — `ch_PP-OCRv4_det_mobile.onnx` + `ch_PP-OCRv4_rec_mobile.onnx` + `ch_ppocr_mobile_v2.0_cls.onnx`
- Chinese + English mixed (`LangRec.CH`)
- Result window with rounded corners + drop shadow (Linear design tokens, embedded so the plugin is self-contained)
- 换行 / 不换行 + 有空格 / 清除空格 segmented toggles (stateful, restore the OCR-original text on toggle-back)
- Inline text editing (TextBox `IsReadOnly=False`)
- CTAs: 复制 / 复制并退出
- Status display: 行数 + 耗时 (ms)

**In-app update flow** (`AboutWindow.xaml.cs` + `Services/UpdateChecker.cs` + `Services/UpdateDownloader.cs`)
- Opening About silently queries GitHub Releases for newer main-app
- Shows current vs. latest + release notes + "更新" button
- Streams installer download with progress bar + cancellable
- Launches installer via `ShellExecute` + calls `Application.Shutdown()` so the old-version uninstaller doesn't collide with the running process
- MainWindow's 关于 menu item gets a "（有版本更新）" badge when an update is found

## Improvements
- All mouse-hover ToolTip prompts removed
- New design tokens: `StatusGreen` (success), `BorderStrong` (1px hairline matching OS chrome)
- OCR window chrome aligned with main app's Linear style (custom `Window.Template` with drop shadow + Linear close/toggle colors)
- About window footer credits split into two lines for readability
- OCR plugin's SkiaSharp (3.119) kept ALC-isolated from main app's (3.116) — different versions, no conflict

## Bug fixes since v2.0.2
- Tree "jump to directory" click was broken on cold start (race against the async drive loader). Fixed by synchronously constructing `DriveItemNode` from `DriveInfo` when not in `Items`, and by skipping the deferred `SelectFirstNode` that overrode our drill.
- Drill view was polluted with C:/D:/E: drive rows after JumpToDirectory. Fixed `LoadDrivesAsync` to bail when the "此电脑" section header is missing (drill mode), and added re-trigger logic in `NavigateBack` when the root view ended up without drives.
- Selected folder was not scrolled into viewport after JumpToDirectory. Replaced unreliable `BringIntoView` (which left the row half-clipped at viewport's bottom edge) with an explicit adaptive scroll: small items centered, large items top-aligned with 24px margin.

## Bundled in this installer

The OCR plugin is **bundled** with the installer — no manual model download required. After install, open "关于" → check "OCR 文字提取" shows green. If red, model files may have been blocked by antivirus during install (rare); reinstall or restore from `quarantine`.

Bundled assets:
- `Plugins\ApertureNeo.Plugins.Ocr.dll`
- `Plugins\Assets\models\paddleocr\` (3 ONNX files, ~16 MB):
  - `ch_PP-OCRv4_det_mobile.onnx` (text detection)
  - `ch_PP-OCRv4_rec_mobile.onnx` (text recognition, Chinese + English)
  - `ch_ppocr_mobile_v2.0_cls.onnx` (direction classifier)
- `Plugins\` native deps (OpenCV 4.13, ONNX Runtime 1.26, SkiaSharp 3.119, RapidOCRSharpOnnx, Clipper2)

## Files
- `ApertureNeo-Setup-v3.0.0.exe` — self-contained win-x64 installer (~150 MB, OCR plugin + models included)