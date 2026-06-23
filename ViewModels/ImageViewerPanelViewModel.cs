using System;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ApertureNeo.Controls;
using ApertureNeo.Models;
using ApertureNeo.Services;

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
    private SkiaImageViewer? _viewer;

    public ImageViewerPanelViewModel(INavigationService navigation, IUiState uiState)
    {
        _navigation = navigation;
        _uiState = uiState;
        // P2: ImageViewerPanelViewModel is the writer of
        // IUiState.CurrentImage — every navigation change
        // publishes the new current item. Other VMs observe
        // IUiState.PropertyChanged for CurrentImage. Keeping
        // the navigation subscription here too so the VM
        // also gets the immediate navigation event (used
        // to push the image-load call to SkiaImageViewer
        // before dimensions are known).
        _navigation.CurrentImageChanged += OnCurrentImageChanged;
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
    }

    [RelayCommand]
    private void Fit() => _viewer?.FitToScreen();

    [RelayCommand]
    private void ZoomToOriginal() => _viewer?.ZoomToOriginal();

    [RelayCommand]
    private void ZoomIn() => _viewer?.ZoomIn();

    [RelayCommand]
    private void ZoomOut() => _viewer?.ZoomOut();
}