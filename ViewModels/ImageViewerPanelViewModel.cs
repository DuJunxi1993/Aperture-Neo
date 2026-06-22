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
    }

    private void OnViewerImageLoaded(ImageLoadResult result)
    {
        // Mirror to IUiState so the info pill can read it
        // without going through MainWindow's controllers.
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