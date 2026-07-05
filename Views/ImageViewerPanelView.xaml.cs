using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ApertureNeo.Controls;
using ApertureNeo.Controls.Annotation;
using ApertureNeo.Services;
using ApertureNeo.ViewModels;
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
    public ImageViewerPanelView()
    {
        InitializeComponent();
        DataContext = AppHost.Services?.GetService<ImageViewerPanelViewModel>();
    }

    public Grid ViewerColumnRef => ViewerColumn;
    public SkiaImageViewer ImageViewerRef => ImageViewer;

    public event EventHandler? CopyPathRequested;
    public event EventHandler? OpenInExplorerRequested;
    public event EventHandler? PrintRequested;
    public event EventHandler? SetWallpaperRequested;

    private void CtxCopyPath_Click(object sender, RoutedEventArgs e) => CopyPathRequested?.Invoke(this, EventArgs.Empty);
    private void CtxOpenInExplorer_Click(object sender, RoutedEventArgs e) => OpenInExplorerRequested?.Invoke(this, EventArgs.Empty);
    private void CtxPrint_Click(object sender, RoutedEventArgs e) => PrintRequested?.Invoke(this, EventArgs.Empty);
    private void CtxSetWallpaper_Click(object sender, RoutedEventArgs e) => SetWallpaperRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>P5: context menu item to toggle annotation mode.
    /// The menu's header text is updated in the ContextMenuOpening
    /// handler below (binding to IsAnnotating + a converter would
    /// also work but the ContextMenu is a ContextMenu, not in the
    /// main visual tree, so it doesn't pick up DataContext changes
    /// reliably).</summary>
    private void CtxAnnotation_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is ImageViewerPanelViewModel vm && vm.ToggleAnnotationCommand.CanExecute(null))
        {
            vm.ToggleAnnotationCommand.Execute(null);
        }
    }

    // P5: pen drawing on the main viewer's canvas. Same code
    // path as the screenshot editor's PenCanvas (the shared
    // AnnotationState handles all the rendering and state, so
    // these handlers are just a thin mouse→state bridge).
    private bool _isDrawing;
    private AnnotationState? State => (DataContext as ImageViewerPanelViewModel)?.AnnotationState;

    private void PenCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (State == null) return;
        var pt = ToImageCoords(e.GetPosition(PenCanvas));
        if (pt == null) return;
        _isDrawing = true;
        State.BeginStroke();
        State.ExtendStroke(pt.Value);
        PenCanvas.CaptureMouse();
    }

    private void PenCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDrawing) return;
        if (e.LeftButton != MouseButtonState.Pressed) return;
        if (State == null) return;
        var pt = ToImageCoords(e.GetPosition(PenCanvas));
        if (pt == null) return;
        State.ExtendStroke(pt.Value);
    }

    private void PenCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDrawing) return;
        _isDrawing = false;
        PenCanvas.ReleaseMouseCapture();
        State?.CommitStroke();
    }

    private void PenCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        // P5: forward wheel events to the viewer with the
        // cursor position as the zoom origin. This keeps wheel
        // zoom working in pen mode (when PenCanvas is the
        // hit-testable element under the cursor).
        var pos = e.GetPosition(ImageViewer);
        ImageViewer.ZoomAtPoint(e.Delta, pos.X, pos.Y);
        e.Handled = true;
    }

    private SKPoint? ToImageCoords(System.Windows.Point pt)
    {
        var zoom = ImageViewer.Zoom;
        if (zoom < 0.001f) return null;
        var worldX = (float)((pt.X - ImageViewer.OffsetX) / zoom);
        var worldY = (float)((pt.Y - ImageViewer.OffsetY) / zoom);
        var state = State;
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
        var state = State;
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