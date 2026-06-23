using System;
using System.Windows;
using System.Windows.Controls;
using ApertureNeo.Controls;
using ApertureNeo.ViewModels;
using Microsoft.Extensions.DependencyInjection;

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
}