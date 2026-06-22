using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ApertureNeo.Services;

namespace ApertureNeo.ViewModels;

/// <summary>
/// State + commands for the floating navigation bar (prev /
/// next / fit / slideshow / fullscreen + image index + zoom %).
///
/// The VM has no direct reference to <see cref="Controls.SkiaImageViewer"/>;
/// viewer-specific actions (Fit / ZoomToOriginal) raise events
/// that the View translates into method calls on the actual
/// viewer instance. This keeps the VM UI-agnostic — same VM
/// could drive a non-Skia viewer in a future shell.
///
/// Reads: <see cref="INavigationService"/>, <see cref="IUiState"/>.
/// </summary>
public partial class FloatingBarViewModel : ObservableObject
{
    private readonly INavigationService _navigation;
    private readonly IUiState _uiState;

    public FloatingBarViewModel(INavigationService navigation, IUiState uiState)
    {
        _navigation = navigation;
        _uiState = uiState;
        _navigation.CurrentImageChanged += OnCurrentImageChanged;
        _navigation.CollectionChanged += OnCollectionChanged;
        // IUiState.CurrentZoom is set by ImageViewerPanelViewModel
        // (which subscribes to SkiaImageViewer.ZoomChanged and
        // pushes the formatted value); this VM reads it back
        // and formats the display string.
        _uiState.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IUiState.CurrentZoom))
                ZoomText = $"{_uiState.CurrentZoom * 100:F0}%";
        };
    }

    [ObservableProperty]
    private string _imageIndexInfo = "0/0";

    [ObservableProperty]
    private string _zoomText = "100%";

    [ObservableProperty]
    private Wpf.Ui.Controls.SymbolRegular _slideshowIcon = Wpf.Ui.Controls.SymbolRegular.Play20;

    private void OnCurrentImageChanged(ApertureNeo.Models.ImageItem? item) => UpdateImageIndex();
    private void OnCollectionChanged() => UpdateImageIndex();

    private void UpdateImageIndex()
    {
        ImageIndexInfo = $"{_navigation.CurrentIndex + 1}/{_navigation.Count}";
    }

    [RelayCommand]
    private void Prev() => _navigation.MovePrevious();

    [RelayCommand]
    private void Next() => _navigation.MoveNext();

    /// <summary>VM doesn't know about SkiaImageViewer; the View
    /// subscribes to this event and calls FitToScreen().</summary>
    [RelayCommand]
    private void Fit() => FitRequested?.Invoke(this, EventArgs.Empty);
    public event EventHandler? FitRequested;

    [RelayCommand]
    private void ZoomToOriginal() => ZoomToOriginalRequested?.Invoke(this, EventArgs.Empty);
    public event EventHandler? ZoomToOriginalRequested;

    [RelayCommand]
    private void ToggleSlideshow()
    {
        _uiState.IsSlideshowRunning = !_uiState.IsSlideshowRunning;
        SlideshowIcon = _uiState.IsSlideshowRunning
            ? Wpf.Ui.Controls.SymbolRegular.Pause24
            : Wpf.Ui.Controls.SymbolRegular.Play20;
    }

    [RelayCommand]
    private void ToggleFullscreen()
    {
        _uiState.IsFullscreen = !_uiState.IsFullscreen;
    }
}