using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ApertureNeo.Controls;

namespace ApertureNeo.Views;

/// <summary>
/// The image-viewer panel: hosts <see cref="SkiaImageViewer"/>
/// + its right-click context menu. Migrated from MainWindow.xaml.
///
/// The host (MainWindow) reads <see cref="ImageViewerRef"/> to
/// load images and drive zoom/fit. Context-menu items raise
/// plain .NET events.
/// </summary>
public partial class ImageViewerPanelView : UserControl
{
    public ImageViewerPanelView()
    {
        InitializeComponent();
    }

    public Grid ViewerColumnRef => ViewerColumn;
    public SkiaImageViewer ImageViewerRef => ImageViewer;

    public event EventHandler? CopyPathRequested;
    public event EventHandler? OpenInExplorerRequested;
    public event EventHandler? PrintRequested;
    public event EventHandler? SetWallpaperRequested;
    public event MouseButtonEventHandler? ViewerPreviewMouseLeftButtonDown;

    private void CtxCopyPath_Click(object sender, RoutedEventArgs e) => CopyPathRequested?.Invoke(this, EventArgs.Empty);
    private void CtxOpenInExplorer_Click(object sender, RoutedEventArgs e) => OpenInExplorerRequested?.Invoke(this, EventArgs.Empty);
    private void CtxPrint_Click(object sender, RoutedEventArgs e) => PrintRequested?.Invoke(this, EventArgs.Empty);
    private void CtxSetWallpaper_Click(object sender, RoutedEventArgs e) => SetWallpaperRequested?.Invoke(this, EventArgs.Empty);

    private void ImageViewer_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        ViewerPreviewMouseLeftButtonDown?.Invoke(this, e);
    }
}