using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using ApertureNeo;
using ApertureNeo.Controls;
using ApertureNeo.Controls.Annotation;
using ApertureNeo.Models;
using ApertureNeo.Services;
using SkiaSharp;

namespace ApertureNeo.ViewModels;

/// <summary>
/// State + commands for the image viewer panel
/// (<see cref="ApertureNeo.Views.ImageViewerPanelView"/>).
///
/// Owns the load-image call, the zoom commands, and the
/// <see cref="IUiState.CurrentZoom"/> mirror. The VM holds a
/// direct reference to the <see cref="SkiaImageViewer"/>
/// instance because that control is created by the View (not
/// from DI) and the zoom commands are the viewer's direct
/// responsibility. The viewer is set by the View via
/// <see cref="SetViewer"/> after the control is loaded.
///
/// Reads: <see cref="INavigationService"/>, <see cref="IUiState"/>.
/// </summary>
public partial class ImageViewerPanelViewModel : ObservableObject
{
    private readonly INavigationService _navigation;
    private readonly IUiState _uiState;
    private readonly ISettingsStore? _settings;
    private SkiaImageViewer? _viewer;
    // P1 fix: timestamp of the last double-click action. Used to
    // debounce rapid additional clicks that WPF reports as part
    // of the same click sequence (ClickCount=3, 4, 5...). Without
    // this, double-click → click → triggers another toggle
    // (because WPF keeps incrementing ClickCount for clicks
    // within the system double-click window).
    private DateTime _lastDoubleClickHandledAt = DateTime.MinValue;
    // Cached system double-click time. WPF 10's SystemParameters
    // doesn't expose MouseDoubleClickTime (it was removed at some
    // point), so we ask the Win32 user32.dll directly via
    // GetDoubleClickTime. The value rarely changes during a
    // session, so cache it on first use. Default 500ms if the
    // call ever returns 0 (which shouldn't happen on Windows).
    private static readonly int _doubleClickTimeMs = GetCachedDoubleClickTime();

    // P5: annotation mode. Local source of truth; mirrored to
    // IUiState.IsAnnotating so the FloatingBar can hide and the
    // annotation toolbar's Visibility binding reacts.
    [ObservableProperty] private bool _isAnnotating;

    // P5: the shared annotation state. Recreated on image
    // change (the OverlayBitmap is dimension-bound). Bound to
    // the AnnotationOverlay's State DP via XAML.
    [ObservableProperty] private AnnotationState? _annotationState;

    public ImageViewerPanelViewModel(INavigationService navigation, IUiState uiState, ISettingsStore? settings = null)
    {
        _navigation = navigation;
        _uiState = uiState;
        _settings = settings;
        // P2: ImageViewerPanelViewModel is the writer of
        // IUiState.CurrentImage — every navigation change
        // publishes the new current item. Other VMs observe
        // IUiState.PropertyChanged for CurrentImage. Keeping
        // the navigation subscription here too so the VM
        // also gets the immediate navigation event (used
        // to push the image-load call to SkiaImageViewer
        // before dimensions are known).
        _navigation.CurrentImageChanged += OnCurrentImageChanged;
        // P5: keep IUiState.IsAnnotating in sync so the
        // FloatingBarViewModel can hide on annotating. Local
        // IsAnnotating is the source of truth; we mirror to
        // the shared state for cross-VM observation.
        _uiState.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IUiState.IsAnnotating))
                IsAnnotating = _uiState.IsAnnotating;
        };
    }

    /// <summary>Called by the View in its Loaded handler.</summary>
    public void SetViewer(SkiaImageViewer viewer)
    {
        if (_viewer != null) return;
        _viewer = viewer;
        _viewer.ZoomChanged += z => _uiState.CurrentZoom = z;
        _viewer.ImageLoaded += OnViewerImageLoaded;
        // P2: handle the viewer double-click fit↔zoom toggle here
        // instead of routing through the host MainWindow's
        // Viewer_PreviewMouseLeftButtonDown controller method.
        // The VM already holds the viewer reference, so the visual
        // tree walk + IsAtFitScale check + ZoomToOriginal /
        // FitToScreen call are all self-contained.
        _viewer.PreviewMouseLeftButtonDown += OnViewerPreviewMouseLeftButtonDown;
        // If the navigation has already happened before the
        // viewer was set, load the current image now.
        if (_navigation.Current != null)
            _viewer.LoadImage(_navigation.Current.FilePath);
    }

    /// <summary>
    /// Double-click on the viewer (outside buttons): toggle the
    /// image between Fit-to-screen and 100% (same as the
    /// floating-bar percent label click semantics, but
    /// round-tripping in both directions). Behaviour is the same
    /// in window mode and fullscreen mode — double-click never
    /// exits fullscreen anymore. Esc / Ctrl+F still does.
    /// Preview (tunneling) phase + e.Handled=true so the event
    /// does not bubble to underlying controls (the edge-nav
    /// arrows in fullscreen, the floating bar in window mode).
    /// </summary>
    private void OnViewerPreviewMouseLeftButtonDown(object? sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ClickCount < 2) return;
        // Don't trigger on the edge-nav buttons, the exit pill, or
        // the floating bar — those have their own click semantics.
        if (e.OriginalSource is System.Windows.DependencyObject src)
        {
            System.Windows.DependencyObject? walker = src;
            while (walker != null)
            {
                if (walker is System.Windows.Controls.Button) return;
                if (walker is System.Windows.Controls.Primitives.ButtonBase) return;
                walker = System.Windows.Media.VisualTreeHelper.GetParent(walker);
            }
        }
        // P1 fix: debounce rapid additional clicks that WPF
        // reports as part of the same click sequence. WPF keeps
        // incrementing ClickCount (3, 4, 5...) for clicks within
        // the system double-click window, so without this guard a
        // double-click → click would re-trigger the toggle (the
        // third click comes in as ClickCount=3 ≥ 2). The window
        // is the OS double-click time (typically 500ms on
        // Windows). After the window elapses, the user has
        // effectively started a fresh sequence; the next
        // double-click is a deliberate new action.
        var now = DateTime.UtcNow;
        if ((now - _lastDoubleClickHandledAt).TotalMilliseconds < _doubleClickTimeMs)
            return;
        _lastDoubleClickHandledAt = now;
        // Toggle between Fit and 100%, in both window mode and
        // fullscreen mode. (Matches the floating-bar percent label
        // click semantics, but in both directions — clicking the
        // percent always zooms to 100%, the viewer double-click
        // rounds-trips fit↔100%.)
        if (_viewer == null) return;
        if (_viewer.IsAtFitScale)
            _viewer.ZoomToOriginal();
        else
            _viewer.FitToScreen();
        e.Handled = true;
    }

    private void OnCurrentImageChanged(ImageItem? item)
    {
        _viewer?.LoadImage(item?.FilePath ?? string.Empty);
        // Mirror the current image to IUiState so the InfoPill /
        // InfoPopover VMs (which observe IUiState.CurrentImage
        // and refresh on change) re-read their data. P2
        // bugfix: without this, the pill stays empty because
        // the navigation event fires BEFORE the image is
        // decoded and the ImageItem's Width/Height are
        // unknown at that point. The InfoPillVM /
        // InfoPopoverVM also subscribe to the item's
        // PropertyChanged (Width / Height) so they re-read
        // the dimensions after the image is decoded.
        _uiState.CurrentImage = item;
    }

    private void OnViewerImageLoaded(ImageLoadResult result)
    {
        var item = _navigation.Items.FirstOrDefault(i => i.FilePath == result.FilePath);
        if (item == null) return;
        item.SetDimensions(result.Width, result.Height);

        // P5: rebuild the AnnotationState for the new image. The
        // OverlayBitmap is sized to the source image; the
        // previous state's bitmap is wrong-sized. Re-entering
        // annotation mode would otherwise write to a stale
        // bitmap (or NRE if the new image is smaller than the
        // old).
        AnnotationState = new AnnotationState(result.Width, result.Height);
        // Set the source bitmap so mosaic mode can read original
        // image pixels instead of averaging the transparent overlay.
        AnnotationState.SourceBitmap = result.Bitmap;
        // Scale pen + mosaic sizes by the host view's DPI so a
        // "20" brush on a 192-DPI display renders as a
        // 40-image-pixel stroke (matching the screen-space size
        // the user sees in the size picker). The view pushes its
        // dpi scale via SetAnnotationDpiScale when it loads; if
        // no value arrives (test harness, etc.) we fall back to
        // 1.0 which preserves the historical stroke-width
        // behaviour.
        AnnotationState.DpiScale = _annotationDpiScale;
    }

    private float _annotationDpiScale = 1.0f;

    /// <summary>Called by <c>ImageViewerPanelView</c> when its
    /// visual tree is realised. Pushes the view's DPI scale into
    /// <see cref="AnnotationState"/> so pen + mosaic strokes
    /// render at a screen-consistent size on hi-DPI displays.</summary>
    public void SetAnnotationDpiScale(float dpiScale)
    {
        _annotationDpiScale = dpiScale <= 0 ? 1.0f : dpiScale;
        if (AnnotationState != null) AnnotationState.DpiScale = _annotationDpiScale;
    }

    // P5: exposes the currently-displayed image's file path so
    // the View can pass it to AnnotationSaveService (enables
    // the "覆盖原图" button in the SaveDialog). Mirrors
    // IUiState.CurrentImage.FilePath, which the VM is the writer of.
    public string? CurrentImagePath => _uiState.CurrentImage?.FilePath;

    /// <summary>在 EditorWindow 编辑完成后重新加载当前图片。
    /// 调用 SkiaImageViewer.LoadImage 会触发异步解码 + 自适应缩放。</summary>
    public void ReloadCurrentImage()
    {
        if (_uiState.CurrentImage?.FilePath is { } path)
            _viewer?.LoadImage(path);
    }

    [RelayCommand]
    private void Fit() => _viewer?.FitToScreen();

    [RelayCommand]
    private void ZoomToOriginal() => _viewer?.ZoomToOriginal();

    [RelayCommand]
    private void ZoomIn() => _viewer?.ZoomIn();

    [RelayCommand]
    private void ZoomOut() => _viewer?.ZoomOut();

    // ================================================================
    // 以下为 view 栏内部标注模式的代码，保留以供将来开发。
    // 当前已被弹出 EditorWindow 的流程替代，IsAnnotating 永远不会
    // 设为 true。所有标注相关属性/命令在此保留不删。
    // ================================================================

    partial void OnIsAnnotatingChanged(bool value)
    {
        _uiState.IsAnnotating = value;
        if (_viewer != null && AnnotationState != null)
        {
            _viewer.OverlayBitmap = value ? AnnotationState.OverlayBitmap : null;
        }
    }

    // P5: when AnnotationState changes (image switched), wire
    // the new state's RedrawRequested to the viewer's
    // InvalidateOverlay. The old state is unsubscribed in the
    // same handler. Without this, drawing on a new image
    // wouldn't repaint because the viewer doesn't observe the
    // SKBitmap's pixel changes (only the InvalidateOverlay
    // method tells it to re-upload).
    partial void OnAnnotationStateChanged(AnnotationState? value)
    {
        // The old state's RedrawRequested is no longer relevant.
        // We can't unsubscribe without keeping a reference, so
        // we accept a one-event penalty on image change (the
        // old state is about to be GC'd anyway).
        if (value != null && _viewer != null)
        {
            value.RedrawRequested += (_, _) => _viewer.InvalidateOverlay();
        }
    }

    [RelayCommand]
    private void ToggleAnnotation()
    {
        // Only meaningful when there's an image to annotate.
        if (_uiState.CurrentImage == null) return;
        IsAnnotating = !IsAnnotating;
    }

    /// <summary>Stage 7: invoked by the AnnotationOverlay's
    /// "退出编辑" button. Just flips <see cref="IsAnnotating"/>
    /// off — the AnnotationState itself (strokes, color, size)
    /// is preserved so re-entering annotation mode brings back
    /// the user's work. Use <see cref="ToggleAnnotation"/> for
    /// the inverse (re-enter).</summary>
    [RelayCommand]
    private void ExitAnnotation()
    {
        IsAnnotating = false;
    }

    /// <summary>Stage F: invoked by the AnnotationOverlay's
    /// "OCR" button. Runs the OCR pipeline against the current
    /// image (composed with the live annotation overlay if
    /// annotation mode is active). Mirrors the screenshot
    /// editor's OCR flow — copy to clipboard, optionally show
    /// the result window per SettingsStore.EditorOcrShowWindow.
    /// Async because OCR is on the order of 1-2s.</summary>
    [RelayCommand]
    private async Task RunOcrOnCurrentImageAsync()
    {
        var path = CurrentImagePath;
        if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) return;
        try
        {
            using var original = new System.Drawing.Bitmap(path);
            // Compose the live overlay (if any) on top of the
            // original so the OCR sees what the user sees.
            System.Drawing.Bitmap composed = original;
            var overlay = AnnotationState;
            if (IsAnnotating && overlay is { OverlayBitmap.Width: > 0, OverlayBitmap.Height: > 0 })
            {
                composed = new System.Drawing.Bitmap(original.Width, original.Height);
                using (var g = System.Drawing.Graphics.FromImage(composed))
                {
                    g.DrawImage(original, 0, 0);
                    using var overlayGdi = SkBitmapToGdi(overlay.OverlayBitmap);
                    g.DrawImage(overlayGdi, 0, 0);
                }
            }

            var settings = AppHost.Services?.GetService(typeof(ApertureNeo.Services.ISettingsStore))
                as ApertureNeo.Services.ISettingsStore;
            var captureService = AppHost.Services?.GetService<CaptureService>();
            if (captureService == null) return;

            await captureService.RunOcrOnBitmapAsync(composed);

            // Free the composed bitmap if we allocated one.
            if (!ReferenceEquals(composed, original)) composed.Dispose();
        }
        catch (Exception ex)
        {
            ApertureNeo.Services.DebugLog.Write("ImageViewerPanelViewModel",
                $"RunOcrOnCurrentImageAsync failed: {ex.Message}");
        }
    }

    private static System.Drawing.Bitmap SkBitmapToGdi(SkiaSharp.SKBitmap sk)
    {
        var w = sk.Width;
        var h = sk.Height;
        var bmp = new System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var data = bmp.LockBits(new System.Drawing.Rectangle(0, 0, w, h),
            System.Drawing.Imaging.ImageLockMode.WriteOnly,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            unsafe
            {
                // GDI+ scans top-down by default; SKBitmap is also
                // top-down, so a single contiguous copy suffices.
                // 4-arg MemoryCopy(void*, void*, int, int) — bytes
                // per row and total bytes (matches EditorWindow's
                // SkBitmapToGdi helper).
                int size = w * h * 4;
                System.Buffer.MemoryCopy(sk.GetPixels().ToPointer(), data.Scan0.ToPointer(), size, size);
            }
        }
        finally
        {
            bmp.UnlockBits(data);
        }
        return bmp;
    }

    // P1: Win32 GetDoubleClickTime returns the system double-click
    // time in milliseconds. We use it as the debounce window for
    // the viewer's double-click fit↔zoom toggle: any additional
    // clicks within this window are treated as part of the same
    // sequence (WPF's ClickCount keeps incrementing for rapid
    // follow-up clicks), so we suppress the second toggle. WPF's
    // own SystemParameters class used to expose this as a static
    // property but the binding is gone in current WPF, so we
    // call user32 directly. Default fallback to 500ms if the
    // call ever returns 0.
    [DllImport("user32.dll")]
    private static extern int GetDoubleClickTime();

    private static int GetCachedDoubleClickTime()
    {
        var t = GetDoubleClickTime();
        return t > 0 ? t : 500;
    }
}