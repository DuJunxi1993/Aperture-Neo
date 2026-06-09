# Aperture Neo

A fast, lightweight WPF image viewer with SkiaSharp rendering, thumbnail sidebar, slideshow, and fullscreen support.

## Download & Install

Grab the latest **ApertureNeo-Setup-v\*.exe** from the [Releases](../../releases) page and run it.

The installer is **fully self-contained**:
- Ships with the .NET 10 Desktop Runtime built-in, so end users do **not** need to install .NET separately
- Single Setup.exe (~64 MB) for x64 Windows 10/11
- Optionally registers image file associations during install
- Detects (and offers to install) WebView2 Runtime, required for the modern FluentWindow UI

### Building from source

Requires Windows + .NET 10 SDK. Inno Setup 6 is also required to produce the installer.

```powershell
# Just the self-contained app output (no installer):
dotnet publish -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -o publish\win-x64

# Full release (app + Setup.exe + optional portable zip):
.\publish.ps1 -Version 1.0.0 -Zip
```

Output:
- `publish\win-x64\ApertureNeo.exe` — self-contained, double-click to run
- `publish\ApertureNeo-Setup-v1.0.0.exe` — Inno Setup installer
- `publish\ApertureNeo-v1.0.0-win-x64-portable.zip` — green/portable archive (with `-Zip`)

## System Requirements

- Windows 10 1809+ / Windows 11, x64
- ~200 MB disk for installation
- WebView2 Runtime recommended (preinstalled on most modern Windows; installer will offer to fetch it)

## Features

- SkiaSharp-powered image decoding and rendering
- Smooth zoom (scroll wheel, keyboard shortcuts, double-click fit/original toggle)
- Thumbnail sidebar with scroll-into-view and selection highlighting
- Slideshow mode
- Fullscreen mode with auto-hiding toolbar
- Right-click context menu (copy path, open in explorer, print, set wallpaper)
- FileSystemWatcher for live folder updates
- Drag-and-drop support
- Command-line argument support
- Thumbnail caching with SQLite

## Supported Formats

jpg, jpeg, png, bmp, gif, tiff, tif, webp, heic, heif, avif, ico

## Keyboard Shortcuts

| Action | Shortcut |
|---|---|
| Open file | `Ctrl+O` |
| Previous image | `←` `↑` |
| Next image | `→` `↓` |
| Zoom in | `Ctrl++` |
| Zoom out | `Ctrl+-` |
| Fit to screen | `Ctrl+0` |
| Toggle slideshow | `F5` |
| Fullscreen | `Ctrl+F` |
| Toggle sidebar | `Ctrl+Shift+P` |
| Exit slideshow / fullscreen | `Esc` |
