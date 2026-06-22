using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ApertureNeo.Models;
using ApertureNeo.Services;

namespace ApertureNeo.ViewModels;

/// <summary>
/// State for the small resolution pill in the top-right of
/// the viewer ("2048 × 1152" + status dot). Pure presentation;
/// no commands — clicks raise a <see cref="PopoverToggleRequested"/>
/// event that the host uses to open / close the popover.
///
/// The popover is a separate UserControl (InfoPopoverView) with
/// its own VM (InfoPopoverViewModel) — they share data via
/// <see cref="IUiState.CurrentImage"/>.
/// </summary>
public partial class InfoPillViewModel : ObservableObject
{
    private readonly INavigationService _navigation;
    private readonly IUiState _uiState;

    public InfoPillViewModel(INavigationService navigation, IUiState uiState)
    {
        _navigation = navigation;
        _uiState = uiState;
        _navigation.CurrentImageChanged += OnCurrentImageChanged;
        // IUiState.IsUpdateAvailable is also observed (for the
        // (有版本更新) suffix on the About menu item — but
        // that's the title bar's concern, not the info pill's).
        // The pill is just the resolution text + a status dot.
        _uiState.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IUiState.CurrentImage))
                UpdateResolution();
        };
        UpdateResolution();
    }

    [ObservableProperty]
    private string _imageInfo = string.Empty;

    [ObservableProperty]
    private bool _isDimensionsKnown;

    private void OnCurrentImageChanged(ImageItem? item) => UpdateResolution();

    private void UpdateResolution()
    {
        var item = _uiState.CurrentImage ?? _navigation.Current;
        if (item?.Width.HasValue == true && item.Height.HasValue == true)
        {
            ImageInfo = $"{item.Width} × {item.Height}";
            IsDimensionsKnown = true;
        }
        else
        {
            ImageInfo = string.Empty;
            IsDimensionsKnown = false;
        }
    }

    /// <summary>Raised when the user clicks the pill. Host opens
    /// / closes the popover.</summary>
    public event EventHandler? PopoverToggleRequested;

    [RelayCommand]
    private void Toggle() => PopoverToggleRequested?.Invoke(this, EventArgs.Empty);
}