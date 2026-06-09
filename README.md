# Aperture Neo

A fast, lightweight WPF image viewer with SkiaSharp rendering, thumbnail sidebar, slideshow, and fullscreen support.

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
