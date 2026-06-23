using System;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ApertureNeo.Helpers;
using ApertureNeo.Models;
using ApertureNeo.Services;

namespace ApertureNeo.ViewModels;

/// <summary>
/// State for the info popover body (file name / size+dimensions
/// / EXIF Make/Model/Date). Populated lazily from the current
/// <see cref="ImageItem"/>; EXIF values are read from the
/// BitmapMetadata probe (lazy) and may arrive after the popover
/// first opens.
///
/// Reads: <see cref="IUiState"/> (CurrentImage) for navigation,
/// <see cref="INavigationService"/> for the current item, and
/// the current item's PropertyChanged for dimension updates.
/// </summary>
public partial class InfoPopoverViewModel : ObservableObject
{
    private readonly IUiState _uiState;
    private readonly INavigationService _navigation;
    private ImageItem? _currentItem;

    public InfoPopoverViewModel(IUiState uiState, INavigationService navigation)
    {
        _uiState = uiState;
        _navigation = navigation;
        // P2 fix: only observe IUiState for navigation signals.
        // _navigation.CurrentImageChanged is unreliable here
        // (the subscription can be lost when the view is in a
        // Popup with a complex visual tree — see the P2 step 4
        // bugfix commit log). IUiState is a singleton and its
        // PropertyChanged event is the central hub that the
        // ImageViewerPanelViewModel writes to on every
        // navigation change.
        _uiState.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IUiState.CurrentImage))
                BindToItem(_uiState.CurrentImage ?? _navigation.Current);
        };
        BindToItem(_uiState.CurrentImage ?? _navigation.Current);
    }

    [ObservableProperty] private string _fileName = "—";
    [ObservableProperty] private string _fileSize = "—";
    [ObservableProperty] private string _dimensions = "—";
    [ObservableProperty] private string _exifMake = "—";
    [ObservableProperty] private string _exifModel = "—";
    [ObservableProperty] private string _exifDate = "—";

    private void BindToItem(ImageItem? item)
    {
        // Unsubscribe from the previous item's PropertyChanged
        // so we don't leak subscriptions across navigations.
        if (_currentItem != null)
            _currentItem.PropertyChanged -= OnItemPropertyChanged;
        _currentItem = item;
        if (item != null)
            item.PropertyChanged += OnItemPropertyChanged;
        Refresh();
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Width / Height fire PropertyChanged when the lazy
        // header-probe lands AND when the authoritative decode
        // overwrites them. EXIF probes (BitmapMetadata)
        // fire separately; we just Refresh() on every
        // PropertyChanged because the popover opens rarely
        // and the cost of re-reading is negligible.
        Refresh();
    }

    /// <summary>Re-read the current image's metadata and push
    /// it into the observable properties.</summary>
    [RelayCommand]
    public void Refresh()
    {
        var item = _currentItem;
        if (item == null)
        {
            FileName = "—"; FileSize = "—"; Dimensions = "—";
            ExifMake = ExifModel = ExifDate = "—";
            return;
        }
        FileName = string.IsNullOrEmpty(item.FileName) ? "—" : item.FileName;
        FileSize = ImageFormatHelper.FormatFileSize(item.FileSize);
        Dimensions = item.Width.HasValue && item.Height.HasValue
            ? $"{item.Width} × {item.Height}"
            : "—";
        var exif = item.Exif;
        if (exif != null)
        {
            var make = item.GetExifValue("/app1/ifd/{ushort=271}");
            var model = item.GetExifValue("/app1/ifd/{ushort=272}");
            var dateTaken = item.GetExifValue("/app1/ifd/{ushort=306}");
            ExifMake = string.IsNullOrEmpty(make) ? "—" : make;
            ExifModel = string.IsNullOrEmpty(model) ? "—" : model;
            ExifDate = string.IsNullOrEmpty(dateTaken) ? "—" : dateTaken;
        }
        else
        {
            ExifMake = ExifModel = ExifDate = "—";
        }
    }
}