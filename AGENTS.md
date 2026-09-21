# Aperture Neo

## Build

```bash
dotnet build
dotnet run
dotnet run -- "C:\path\to\image.jpg"   # open a file at startup
```

- Solution file (`ApertureNeo.slnx`) includes all projects. Use `dotnet build` (auto-detects `.slnx`) to build the main exe + both plugins (`Plugins.Screenshot`, `Plugins.Ocr`) in one command. Without the solution, `dotnet build` only builds the main exe and plugin DLLs won't be in `bin/Debug/net10.0-windows/Plugins/`, causing missing hotkeys and missing context menu items.
- Windows-only: `net10.0-windows` + WPF. Do not target `AnyCPU` — `<PlatformTarget>x64</PlatformTarget>` and `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` are set in `ApertureNeo.csproj` and required by SkiaSharp.
- Output: `bin/Debug/net10.0-windows/ApertureNeo.exe`
- Thumbnails persist in `%TEMP%\ApertureNeo\thumbs\cache.db`; user settings in `%APPDATA%\ApertureNeo\settings.json`.
- `Installer/` holds packaging artifacts; not part of the build graph.

## Dependencies

- **WPF-UI 3.0.5** — FluentWindow, dark Mica backdrop
- **SkiaSharp 3.116.1** — image decoding and rendering
- **Microsoft.Data.Sqlite 8.0.10** + **SQLitePCLRaw.bundle_e_sqlite3 2.1.10** — thumbnail cache DB; `Batteries_V2.Init()` must run before any `SqliteConnection` opens (called in `App.OnStartup`, do not reorder).

## Architecture

| Folder | Purpose |
|--------|---------|
| `Controls/` | SkiaImageViewer (SkiaSharp rendering), ThumbnailGrid (multi-column thumbnail grid), AppCommands |
| `Controls/FolderTree/` | FolderTreeView, TreeNodeBase + FolderNode / DriveNode / RootNode (lazy-load) |
| `Services/` | ImageLoader (async decode with max-dim clamp), NavigationService (folder + FSW, ObservableCollection), SlideshowService, ThumbnailCache (SQLite with memory fallback), SettingsStore (JSON) |
| `Models/` | ImageItem (with BitmapSource Thumbnail), ImageLoadResult, RecentEntry |
| `Helpers/` | FormatHelper (supported extensions) |

Entry point: `MainWindow.xaml` / `MainWindow.xaml.cs`. Three-column layout: **FolderTree | ThumbnailGrid | SkiaImageViewer**. Tree and thumbnail columns are togglable via buttons in the title bar (and hidden during fullscreen). Tree and thumbnails can also be hidden/shown via buttons; column widths are draggable via `GridSplitter` (not persisted).

Shared singletons on `App`:
- `App.ThumbnailCache` — single SQLite-backed cache (falls back to in-memory if SQLite fails)
- `App.SettingsStore` — favorites + recent (max 10)

## Key Behaviors

- **Image decoding**: ImageLoader clamps largest dimension to `_maxDecodeDimension` (default 7680). Images above limit are downscaled.
- **Thumbnail caching**: 200×200 JPEG quality 85, cached to SQLite keyed by path + mtime. `MaxEntries = 2000` (LRU eviction by `created_at`). Concurrency: `SemaphoreSlim(MaxConcurrentThumbDecodes=4)`. Falls back to in-memory generation if SQLite read/write fails.
- **Tree**: 3 fixed roots (Favorites / Recent / ThisPC). Lazy-loads subdirectories on expand via `Directory.EnumerateDirectories` (hidden files skipped). Right-click a folder node → add/remove favorite. Favorites and recent auto-refresh when SettingsStore changes.
- **Navigation**: wraps around (MoveNext/MovePrevious modulo count). FileSystemWatcher reloads folder on create/delete/rename.
- **Zoom**: smooth animated zoom (0.05x–20x) with scroll-wheel zoom-to-cursor and double-click fit/original toggle.
- **Fullscreen** (Ctrl+F): hides title bar, bottom bar, both side columns (tree + thumbnails). Toolbar auto-hides after 2.5s idle; mouse-move shows it again. Esc exits.
- **Floating bar proximity expand**: the bottom-center control bar collapses into an iOS-style handle (40×5 pill) at the bottom. Hovering the handle or its invisible 140×20 hot zone expands the bar by scaling it up out of the handle anchor (`RenderTransformOrigin="0.5,1.1"`, 200ms ease-out, handle fades out); leaving the bar collapses it back onto the handle (400ms grace delay, 30s idle fallback, 250ms ease-in). The zoom slider popup keeps the bar expanded while open. Hit-testing is disabled while collapsed. Implemented in `FloatingBarView.xaml.cs`; fullscreen hides the bar + handle via `FloatingBarContentRef` / `HandleRef` in `UpdateOverlayVisibility` (exit re-collapses via `CollapseIfPointerAway`).
- **SkiaImageViewer**: uses `SKSurface` + `WriteableBitmap` for WPF interop; directional navigation (next/prev) uses a touch-gallery style 420ms parallel slide: the old bitmap exits from its own captured fit position toward the trailing edge while the new bitmap enters from the opposite edge with its own fit (ease-in-out cubic — velocity zero at both ends; no cross-scaling); non-directional loads (startup, editor return, cross-folder jumps) swap instantly. Direction comes from `TransitionDirection` plumbed through `LoadImage`/`LoadPreDecoded`; the VM derives it from the index delta of the last load. Regular zoom/fit animations run 180ms smoothstep. Animation frames draw with `SKFilterQuality.Low` (bilinear, CPU cost halved for fullscreen slide frame budget); the resting frame snaps back to High (via `_animating` in `RenderToWriteableBitmap`).
- **Image load order**: thumbnails load in priority order (closest to current index first) via distance-based sort; `CancellationTokenSource` cancels in-flight loads on folder change.
- **Data migration**: on first run after the rename, `MigrateLegacyData()` automatically moves old `ImageViewerNeo` and `HighSpeedImageViewer` cache/settings to the new `ApertureNeo` paths.

## No Test Suite

No test project or test framework present. Do not attempt to run tests.
