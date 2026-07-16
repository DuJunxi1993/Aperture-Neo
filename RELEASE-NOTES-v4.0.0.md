# Aperture Neo 4.0.0

## Highlights

- **Editor rewrite** — the screenshot editor has been fully rewritten with SkiaSharp rendering, moving from the old `Plugins.Screenshot/EditorWindow` to a first-class `Views/EditorWindow` with annotation tools, zoom slider, title bar, resize & maximize support.
- **In-process screenshot service** — `CaptureEngine` extracted from the Screenshot plugin into a shared `Services/CaptureService`, eliminating the standalone `ScreenshotTool.exe` pipeline for in-app captures. Supports area, window, and fullscreen capture.
- **OCR pipeline** — PaddleOCR v4 ONNX models bundled, with an `OcrService`, `OcrPlugin`, and `OcrResultWindow` for extracting text from images via the viewer context menu or annotation toolbar.
- **Plugin framework** — `IPluginModule` / `IPluginContext` / `PluginLoader` architecture with ALC isolation, ViewSlot mechanism for contributing UI (e.g. viewer context menu), and a Settings panel for toggling plugins on/off.
- **Settings center** — `SettingsWindow` with plugin management, keyboard shortcut remapping, favorites, and a `FirstRunDialog` for onboarding.
- **Linear design language** — unified visual system across all windows (HarmonyOS Sans SC fonts, design tokens, SurfaceFloating / SurfaceElevated / SurfaceCard surfaces, ShadowDialog, consistent corner radii).
- **Viewer zoom slider popup** — click the zoom percentage in the floating bar to open an inline slider (5%–1000%) above the bar, matching the Editor's zoom experience.
- **Main window modularization** — monolithic code-behind refactored into ViewModels + UserControls: `FloatingBarView`, `ImageViewerPanelView`, `FolderTreePanelView`, `ThumbnailPanelView`, `TitleBarView`, `EdgeNavView`, `InfoPillView`, `InfoPopoverView`.

## New

- **EditorWindow** (`Views/EditorWindow.xaml` / `.cs`): SkiaSharp-based image editor with pen (color), mosaic (blur), undo/clear, zoom slider (50%–200%), Fit button, OCR integration, and save dialog. Chrome-less title bar with Fluent UI filled-ring icons. Window resize handles (4px/8px), maximize-to-work-area constraint via `WM_GETMINMAXINFO`.
- **Screenshot capture region overlay** (`Views/RegionOverlay.xaml` / `.cs`): semi-transparent overlay with drag-select, crosshair cursor, and a floating toolbar (confirm/cancel). Supports area/window/fullscreen modes.
- **OCR subsystem** (`Plugins.Ocr.Core/`, `Plugins.Ocr/`, `Plugins.Ocr.Ui/`): PaddleOCR v4 ONNX inference (detection + classification + recognition), `OcrResultWindow` with text display and per-line copy, headless `OcrService` for CLI `aperture ocr`, viewer context menu "提取当前图片文字".
- **SettingsWindow** (`Views/SettingsWindow.xaml` / `.cs`): tabbed settings with General, 快捷键 (shortcut remapping via `ShortcutRecorder`), and 插件 (plugin enabled/disabled toggles).
- **Shortcut service** (`Services/GlobalHotkeyService.cs` + `IShortcutService`): Win32 `RegisterHotKey`-based global hotkeys with conflict detection and user notification.
- **Single instance** (`Services/SingleInstance.cs`): mutex-based single-instance enforcement with argument forwarding to the running instance via named pipe.
- **Tray service** (`Services/TrayService.cs`): system tray icon with screenshot and quit context menu, notification area integration.
- **Auto-start** (`Services/AutoStartService.cs`): per-user registry-based auto-start registration via `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.
- **Font system** — HarmonyOS Sans SC (5 weights) bundled and applied as `FontPrimary` / `FontMono` across all windows.
- **Design tokens** (`DesignTokens.xaml`): centralized `RadiusPanel` (12), `RadiusCard` (8), brand colors, surface colors, border colors, shadow effects. Replaces scattered hardcoded values.

## Changed

- **Screenshot plugin** (`Plugins.Screenshot/ScreenshotPlugin.cs`): rewired to use in-process `CaptureService` instead of launching `ScreenshotTool.exe`. Registers global hotkeys (`Ctrl+Alt+A` area, `Ctrl+Alt+T` OCR, `PrintScreen` fullscreen). Contributes "截图 (区域)" to viewer context menu.
- **Viewer context menu** restructured: removed Separator between built-in items and plugin items. All items in a single group. Plugin items insert after the separator group (not before).
- **Floating bar zoom label**: single-click now opens the zoom slider popup (was `ZoomToOriginal`). Reset to 100% via the slider.
- **Slider track styling** (`Styles/Annotation.xaml`): DecreaseRepeatButton blue fill constrained to 4px height (matching the gray track background), eliminating the 5px visual bulge above/below. Thumb remains 14px.
- **AssemblyInfo.cs**: version synchronized to 4.0.0.0 across all three version attributes.
- **publish.ps1**: plugin source path fixed from `bin\AnyCPU\$Configuration\` to `bin\$Configuration\`.

## Fixed

- **EditorWindow maximized window clipping**: constrain to `Screen.WorkingArea` via `WM_GETMINMAXINFO`; remove margin/shadow/corner-radius on maximize.
- **EditorWindow title bar**: `LinearToolButton` → `LinearWindowButton` (28px, no padding) fixes maximize icon clipping; Fluent UI filled-ring paths for all 4 window buttons (`Dismiss12`, `Subtract12`, `Add12`, `Subtract12`); `WindowChrome.WindowChrome` removed for native title bar behavior.
- **EditorWindow bottom bar corner**: `CornerRadius="0,0,8,8"` on bottom Border fixes window corner clipping.
- **SettingsWindow not opening**: `{StaticResource ShadowDialog}` property element syntax fixed to `<StaticResource ResourceKey="ShadowDialog"/>` in both `SettingsWindow.xaml` and `FirstRunDialog.xaml`.
- **Editor zoom slider drag area**: `PART_Track` Height 4→14 for comfortable mouse interaction.
- **Default startup layout**: `MainWindow.xaml` column widths changed to `1*/1*/4*` star ratio; viewer height calculated as `Min(1000, workArea.Height*0.8)` for 1:1 initial view.
- **OcrCommand.cs clipboard thread**: `CopyToClipboardAsync` changed to instance async + `Dispatcher.InvokeAsync` marshal for thread-safe `Clipboard.SetText`.
- **Resource disposal**: `CaptureService` OCR path `bitmap.Dispose()` moved to `finally` block; `SettingsStore.Load()` double-check locking; `SettingsStore.Save()` `_saveLock` for concurrent writes; `SlideshowService` `System.Timers.Timer` → `DispatcherTimer`; `NavigationService` 300ms debounce on `FileSystemWatcher`; `SkiaImageViewer.AbortAnimations` disposes paint/surface; `OverlayBitmap` setter disposes old value.
- **.wbmp format**: added to `SupportedExtensions`, `OpenWithProgids`, default association, OCR shell verb, and uninstall cleanup.
- **Viewer context menu button height**: Separator Margin `12,3` + "编辑图片" `MinHeight="36"` for consistent item height across all menu entries.
- **Viewer context menu spacing**: plugin items now insert after the last Separator (was before), fixing alignment with "编辑图片".
- **Main window `ImageContext_Edit`** (`Views/ImageViewerPanelView.xaml`): removed unused `x:Name` and redundant `MinHeight`.

## Technical

- `net10.0-windows` + WPF, `PlatformTarget=x64`, `AllowUnsafeBlocks=true` (SkiaSharp requirement).
- SkiaSharp 3.116.1, WPF-UI 3.0.5, Microsoft.Data.Sqlite 8.0.10, SQLitePCLRaw.bundle_e_sqlite3 2.1.10, CommunityToolkit.Mvvm 8.4.0.
- Plugin architecture via ALC isolation — `Plugins.Screenshot.dll`, `Plugins.Ocr.dll`, `Plugins.Ocr.Core.dll`, `Plugins.Ocr.Ui.dll`.
- OCR: ONNX Runtime + PaddleOCR v4 (detection `ch_PP-OCRv4_det_mobile.onnx`, classification `ch_ppocr_mobile_v2.0_cls.onnx`, recognition `ch_PP-OCRv4_rec_mobile.onnx`).
- Thumbnail cache: SQLite (LRU 2000 entries, 200×200 JPEG quality 85) with in-memory fallback.
- Installer: Inno Setup 6, bilingual (English + 简体中文), PATH integration, OCR verb registration, uninstall cleanup.

## Files

- `ApertureNeo-Setup-v4.0.0.exe` — self-contained win-x64 installer (~226 MB + OCR ONNX models, .NET 10 self-contained)
- `RELEASE-NOTES-v4.0.0.md` (this file)
