using System;
using System.IO;
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
/// Reads: <see cref="IUiState"/> (CurrentImage), <see cref="INavigationService"/>.
/// </summary>
public partial class InfoPopoverViewModel : ObservableObject
{
    private readonly IUiState _uiState;
    private readonly INavigationService _navigation;

    public InfoPopoverViewModel(IUiState uiState, INavigationService navigation)
    {
        _uiState = uiState;
        _navigation = navigation;
        _uiState.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IUiState.CurrentImage))
                Refresh();
        };
        Refresh();
    }

    [ObservableProperty] private string _fileName = "—";
    [ObservableProperty] private string _fileSize = "—";
    [ObservableProperty] private string _dimensions = "—";
    [ObservableProperty] private string _exifMake = "—";
    [ObservableProperty] private string _exifModel = "—";
    [ObservableProperty] private string _exifDate = "—";

    /// <summary>Re-read the current image's metadata and push
    /// it into the observable properties.</summary>
    [RelayCommand]
    public void Refresh()
    {
        var item = _uiState.CurrentImage ?? _navigation.Current;
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