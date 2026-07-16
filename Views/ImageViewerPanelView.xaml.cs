using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ApertureNeo.Controls;
using ApertureNeo.Controls.Annotation;
using ApertureNeo.Services;
using ApertureNeo.ViewModels;
using ApertureNeo.Views.Shared;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;

namespace ApertureNeo.Views;

/// <summary>
/// The image-viewer panel: hosts <see cref="SkiaImageViewer"/>
/// + its right-click context menu. P2: binds to
/// <see cref="ImageViewerPanelViewModel"/> via DataContext;
/// the VM's SetViewer(viewer) is called by MainWindow (which
/// owns the viewer instance) in its WireImageViewerPanelVM()
/// helper after Loaded fires.
///
/// P2 step 9: the viewer double-click fit↔zoom toggle is now
/// owned by <see cref="ImageViewerPanelViewModel"/>. The VM
/// hooks <see cref="SkiaImageViewer.PreviewMouseLeftButtonDown"/>
/// in its SetViewer call, so this view no longer raises
/// ViewerPreviewMouseLeftButtonDown (the event was only
/// consumed by MainWindow's Viewer_PreviewMouseLeftButtonDown
/// controller method, now deleted).
///
/// Context-menu items still raise plain .NET events (they
/// don't have a clean VM target — file-system / OS dialogs).
/// </summary>
public partial class ImageViewerPanelView : UserControl
{
    /// <summary>Shared mouse handling for color and mosaic modes.
    /// Constructed lazily on Loaded so the AnnotationState
    /// (provided by the VM after image load) is available.</summary>
    private PenCanvasBehavior? _penBehavior;

    public ImageViewerPanelView()
    {
        InitializeComponent();
        DataContext = AppHost.Services?.GetService<ImageViewerPanelViewModel>();
        Loaded += (_, _) =>
        {
            // Push the view's effective DPI into the VM so the
            // AnnotationState sizes pen + mosaic strokes in
            // screen-space (a "20" brush on a 192-DPI display
            // renders as a 40-image-pixel stroke). Must happen
            // before InitPenBehavior so the behaviour sees the
            // scaled size from the very first BeginStroke.
            if (DataContext is ImageViewerPanelViewModel vm)
            {
                var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
                // PixelsPerDip on WPF's DpiScale struct is ALREADY the
                // scale factor (1.0 at 96 DPI, 1.25 at 120 DPI / 125%
                // scaling, 2.0 at 192 DPI). The previous formula divided
                // by 96 again, collapsing the value to ~0.013 on a
                // 125%-scaled system and making every pen stroke +
                // mosaic block invisible. Use the property directly.
                vm.SetAnnotationDpiScale(dpi.PixelsPerDip > 0.5
                    ? (float)dpi.PixelsPerDip : 1.0f);
            }
            InitPenBehavior();
        };
    }

    public Grid ViewerColumnRef => ViewerColumn;
    public SkiaImageViewer ImageViewerRef => ImageViewer;

    public event EventHandler? CopyPathRequested;
    public event EventHandler? OpenInExplorerRequested;
    public event EventHandler? PrintRequested;
    public event EventHandler? SetWallpaperRequested;

    private void InitPenBehavior()
    {
        if (_penBehavior != null) return;
        var state = (DataContext as ImageViewerPanelViewModel)?.AnnotationState;
        if (state == null) return;
        _penBehavior = new PenCanvasBehavior(state, MosaicPreviewRect, ToImageCoords);
        _penBehavior.Mode = (PenCanvasBehavior.AnnotationMode)(int)AnnotationBar.Mode;
    }

    private void CtxCopyPath_Click(object sender, RoutedEventArgs e) => CopyPathRequested?.Invoke(this, EventArgs.Empty);
    private void CtxOpenInExplorer_Click(object sender, RoutedEventArgs e) => OpenInExplorerRequested?.Invoke(this, EventArgs.Empty);
    private void CtxPrint_Click(object sender, RoutedEventArgs e) => PrintRequested?.Invoke(this, EventArgs.Empty);
    private void CtxSetWallpaper_Click(object sender, RoutedEventArgs e) => SetWallpaperRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>右键编辑图片：弹出 EditorWindow 编辑当前图片。
    /// 不再使用 view 栏内部的 AnnotationOverlay 标注模式
    /// （标注相关代码保留以供将来开发）。</summary>
    private void CtxAnnotation_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ImageViewerPanelViewModel vm) return;
        var path = vm.CurrentImagePath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

        // 复制到临时文件以避免锁定原始图片
        var tempDir = Path.Combine(Path.GetTempPath(), "ApertureNeo", "edit-temp");
        Directory.CreateDirectory(tempDir);
        var tempPath = Path.Combine(tempDir, $"edit-{Guid.NewGuid():N}.png");
        try
        {
            File.Copy(path, tempPath, overwrite: true);
            using var bitmap = new System.Drawing.Bitmap(tempPath);
            var settings = AppHost.Services?.GetService<ISettingsStore>() as ISettingsStore;
            var editor = new EditorWindow(bitmap, settings)
            {
                OriginalFilePath = path
            };
            var mainWindow = Window.GetWindow(this);
            if (mainWindow != null) editor.Owner = mainWindow;

            editor.ShowDialog();

            // 编辑完成后重新加载图片（可能已被覆盖或另存）
            vm.ReloadCurrentImage();
        }
        finally
        {
            try { File.Delete(tempPath); } catch { }
        }
    }

    // ================================================================
    // 以下为 view 栏内部标注模式的代码，暂时保留以供将来开发。
    // 当前已被弹出 EditorWindow 的流程替代，所有标注功能均不再
    // 从右键菜单激活。（IsAnnotating 永远不会设为 true，因此
    // AnnotationOverlay、PenCanvas 及其事件处理器均保持闲置。）
    // ================================================================

    private void PenCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_penBehavior == null) InitPenBehavior();
        _penBehavior?.OnMouseLeftButtonDown(sender, e);
    }

    private void PenCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        _penBehavior?.OnMouseMove(sender, e);
        UpdateMosaicPreviewPosition();
    }

    private void PenCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        => _penBehavior?.OnMouseLeftButtonUp(sender, e);

    private void AnnotationBar_ModeChanged(object? sender, AnnotationOverlay.AnnotationMode mode)
    {
        if (_penBehavior == null) return;
        _penBehavior.Mode = (PenCanvasBehavior.AnnotationMode)(int)mode;
        UpdateMosaicPreviewPosition();
    }

    private void UpdateMosaicPreviewPosition()
    {
        if (_penBehavior?.CurrentMosaicRect is not { } rect)
        {
            MosaicPreviewRect.Visibility = Visibility.Collapsed;
            return;
        }
        var zoom = ImageViewer.Zoom;
        if (zoom < 0.001f) return;
        var offX = ImageViewer.OffsetX;
        var offY = ImageViewer.OffsetY;
        var x = rect.Left * zoom + offX;
        var y = rect.Top * zoom + offY;
        System.Windows.Controls.Canvas.SetLeft(MosaicPreviewRect, x);
        System.Windows.Controls.Canvas.SetTop(MosaicPreviewRect, y);
        MosaicPreviewRect.Visibility = Visibility.Visible;
    }

    private void PenCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        var pos = e.GetPosition(ImageViewer);
        ImageViewer.ZoomAtPoint(e.Delta, pos.X, pos.Y);
        e.Handled = true;
    }

    private SKPoint? ToImageCoords(System.Windows.Point pt)
    {
        var zoom = ImageViewer.Zoom;
        if (zoom < 0.001f) return null;
        var offX = ImageViewer.OffsetX;
        var offY = ImageViewer.OffsetY;
        var worldX = (float)((pt.X - offX) / zoom);
        var worldY = (float)((pt.Y - offY) / zoom);
        var state = (DataContext as ImageViewerPanelViewModel)?.AnnotationState;
        if (state == null) return null;
        if (worldX < 0 || worldY < 0 || worldX >= ImageViewer.BitmapSize.Width || worldY >= ImageViewer.BitmapSize.Height) return null;
        return new SKPoint(worldX, worldY);
    }

    /// <summary>P5: Save clicked in the annotation toolbar.
    /// Compose the original image + overlay via the shared
    /// SaveService and show the SaveDialog. The viewer's
    /// "currentFilePath" is the path of the image being
    /// viewed, so the "覆盖原图" button is enabled (and writes
    /// back to the same file the user is annotating).</summary>
    private void AnnotationBar_SaveRequested(object? sender, EventArgs e)
    {
        var state = (DataContext as ImageViewerPanelViewModel)?.AnnotationState;
        if (state == null) return;
        var currentPath = (DataContext as ImageViewerPanelViewModel)?.CurrentImagePath;
        var saveService = new AnnotationSaveService(GetSettings());
        // Load the original bitmap from disk. For a more
        // efficient path the viewer could cache the SKBitmap
        // and we re-wrap, but the disk read is small compared
        // to the OCR/Save dialog interaction time.
        using var original = LoadOriginalBitmap(currentPath);
        if (original == null) return;
        var outcome = saveService.Save(original, state, Window.GetWindow(this), currentFilePath: currentPath);
        // Status feedback could be added via the InfoPill /
        // IUiState; for now we just rely on the dialog closing.
        _ = outcome;
    }

    /// <summary>Stage 7: 退出编辑 clicked in the annotation
    /// toolbar. Flips <c>IsAnnotating</c> off via the VM's
    /// command — the annotation state itself is preserved,
    /// so re-entering annotation mode (via the right-click
    /// "编辑图片" item) brings back the user's strokes.</summary>
    private void AnnotationBar_ExitRequested(object? sender, EventArgs e)
    {
        if (DataContext is ImageViewerPanelViewModel vm &&
            vm.ExitAnnotationCommand.CanExecute(null))
        {
            vm.ExitAnnotationCommand.Execute(null);
        }
    }

    /// <summary>Stage F: OCR clicked in the annotation
    /// toolbar. Composes the original image + overlay and runs
    /// the OCR pipeline. Mirrors the screenshot editor's
    /// OCR button.</summary>
    private void AnnotationBar_OcrRequested(object? sender, EventArgs e)
    {
        if (DataContext is not ImageViewerPanelViewModel vm) return;
        if (vm.RunOcrOnCurrentImageCommand.CanExecute(null))
        {
            vm.RunOcrOnCurrentImageCommand.Execute(null);
        }
    }

    private ISettingsStore? GetSettings()
    {
        return AppHost.Services?.GetService(typeof(ISettingsStore)) as ISettingsStore;
    }

    private static System.Drawing.Bitmap? LoadOriginalBitmap(string? path)
    {
        if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) return null;
        try
        {
            // Read the original file and let GDI+ decode it.
            // For JPEGs/PNGs this is fast. The composed final
            // bitmap goes through the SKOverlay in the save
            // service, so the source type doesn't matter.
            return new System.Drawing.Bitmap(path);
        }
        catch
        {
            return null;
        }
    }
}