using System.ComponentModel;
using ApertureNeo.Models;

namespace ApertureNeo.Services;

/// <summary>
/// Cross-VM shared UI state. Multiple ViewModels need to
/// read or mutate the same flags (e.g. <see cref="IsFullscreen"/>
/// affects the visibility of the title bar, floating bar, info
/// pill, edge nav, and exit hint; <see cref="CurrentImage"/>
/// drives the info pill text + popover fields). Rather than
/// have each VM duplicate state and try to keep them in sync,
/// they all observe a single <see cref="IUiState"/> instance
/// registered in the DI container as a singleton.
///
/// The concrete <see cref="UiState"/> uses CommunityToolkit.Mvvm
/// <c>[ObservableProperty]</c> source generation, so any
/// subscriber's <see cref="INotifyPropertyChanged.PropertyChanged"/>
/// fires automatically when a property changes.
///
/// State vs. service: IUiState holds *current* values that the
/// UI binds to. Long-lived state (favorites, recent folders,
/// thumbnails) stays in the dedicated services
/// (<see cref="ISettingsStore"/>, <see cref="IThumbnailCache"/>,
/// <see cref="NavigationService"/>). IUiState is the
/// "now playing" snapshot.
/// </summary>
public interface IUiState : INotifyPropertyChanged
{
    bool IsFullscreen { get; set; }
    bool IsSlideshowRunning { get; set; }
    bool IsTreeVisible { get; set; }
    bool IsThumbVisible { get; set; }
    ImageItem? CurrentImage { get; set; }
    int CurrentImageIndex { get; set; }
    int ImageCount { get; set; }
    double CurrentZoom { get; set; }
    bool IsUpdateAvailable { get; set; }
}