using System;
using System.ComponentModel;
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
/// Reads: <see cref="IUiState"/> (CurrentImage) for navigation,
/// <see cref="INavigationService"/> for the current item, and
/// the current item's PropertyChanged for dimension updates.
/// The dimensions are populated lazily: the header-probe may
/// set them right after navigation, and the authoritative
/// ImageLoaded handler overwrites them with the decoded
/// values. Both events fire PropertyChanged on the item, so
/// the VM re-reads.
/// </summary>
public partial class InfoPillViewModel : ObservableObject
{
    private readonly INavigationService _navigation;
    private readonly IUiState _uiState;
    private ImageItem? _currentItem;

    public InfoPillViewModel(INavigationService navigation, IUiState uiState)
    {
        _navigation = navigation;
        _uiState = uiState;
        // P2 fix: only observe IUiState for navigation signals.
        // _navigation.CurrentImageChanged is unreliable in this
        // architecture — see InfoPopoverViewModel for the full
        // explanation. IUiState is a singleton and ImageViewer
        // PanelViewModel writes to IUiState.CurrentImage on every
        // navigation change.
        _uiState.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IUiState.CurrentImage))
                BindToItem(_uiState.CurrentImage ?? _navigation.Current);
        };
        BindToItem(_uiState.CurrentImage ?? _navigation.Current);
    }

    [ObservableProperty]
    private string _imageInfo = string.Empty;

    [ObservableProperty]
    private bool _isDimensionsKnown;

    private void OnCurrentImageChanged(ImageItem? item) => BindToItem(item);

    private void BindToItem(ImageItem? item)
    {
        // Unsubscribe from the previous item's PropertyChanged
        // before subscribing to the new one. Without this, the
        // VM leaks subscriptions across navigations.
        if (_currentItem != null)
            _currentItem.PropertyChanged -= OnItemPropertyChanged;
        _currentItem = item;
        if (item != null)
            item.PropertyChanged += OnItemPropertyChanged;
        UpdateResolution();
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // The lazy header-probe fires PropertyChanged for
        // Width / Height before the VM binds; the authoritative
        // ImageLoaded event overwrites them later (also via
        // PropertyChanged). Both paths re-read the values.
        if (e.PropertyName == nameof(ImageItem.Width) ||
            e.PropertyName == nameof(ImageItem.Height))
            UpdateResolution();
    }

    private void UpdateResolution()
    {
        var item = _currentItem;
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