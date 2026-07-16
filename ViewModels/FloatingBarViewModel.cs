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
    // SkiaImageViewer reference is set by MainWindow (after the
    // viewer's Loaded event) via SetViewer. Both Fit and
    // ZoomToOriginal commands route through this viewer — the VM
    // is UI-agnostic for everything else (image index, zoom
    // text, prev/next) but viewer-specific actions like fit/zoom
    // need the actual control. We keep the reference nullable so
    // the VM can be constructed before the viewer is loaded.
    private ApertureNeo.Controls.SkiaImageViewer? _viewer;

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
            // P5: hide the floating bar while the user is in
            // annotation mode (the AnnotationOverlay at the top
            // takes over the toolbar role and the bar would
            // fight the pen for the bottom edge).
            else if (e.PropertyName == nameof(IUiState.IsAnnotating))
                OnPropertyChanged(nameof(IsVisible));
        };
    }

    /// <summary>
    /// P5: drives the bar's Visibility. False when the user is
    /// annotating (the AnnotationOverlay at the top of the
    /// viewer takes over). The View binds Visibility to this
    /// property (with a BoolToVisibility converter already in
    /// App.xaml).
    /// </summary>
    public bool IsVisible => !_uiState.IsAnnotating;

    /// <summary>Called by MainWindow after ViewerPanel.ImageViewerRef
    /// is available. The same instance is also handed to
    /// ImageViewerPanelViewModel; sharing it across the two VMs
    /// keeps Fit/Zoom commands pointing at the same viewer the
    /// double-click handler is hooked on.</summary>
    public ApertureNeo.Controls.SkiaImageViewer? Viewer => _viewer;
    public void SetViewer(ApertureNeo.Controls.SkiaImageViewer viewer) => _viewer = viewer;

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

    // Fit and ZoomToOriginal route to the viewer. The buttons
    // in FloatingBarView.xaml are Command-bound to these
    // [RelayCommand]s (FitCommand / ZoomToOriginalCommand); the
    // 100% label's MouseBinding also calls ZoomToOriginalCommand.
    // Both methods no-op gracefully if the viewer hasn't been
    // set yet (race between VM construction and View's Loaded).
    [RelayCommand]
    private void Fit() => _viewer?.FitToScreen();

    [RelayCommand]
    private void ZoomToOriginal() => _viewer?.ZoomToOriginal();

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