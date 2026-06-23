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
        // If the navigation has already happened before the
        // viewer was set, load the current image now.
        if (_navigation.Current != null)
            _viewer.LoadImage(_navigation.Current.FilePath);
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