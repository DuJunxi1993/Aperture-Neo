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

    /// <summary>
    /// True when the user is in annotation mode (drawing on the
    /// image). Viewers and overlays observe this to hide the
    /// navigation FloatingBar (so the toolbar isn't fighting
    /// the pen for screen real estate) and show the annotation
    /// toolbar instead. Persisted state is not appropriate here
    /// — annotation mode is a transient UI mode that should
    /// reset to false on app restart.
    /// </summary>
    bool IsAnnotating { get; set; }

    /// <summary>
    /// Transient status message shown as a toast over the viewer
    /// (decode failures, invalid open selections, ...). Any VM can
    /// publish one; the image viewer VM mirrors it for display and
    /// auto-clears it after a few seconds. Empty string = hidden.
    /// </summary>
    string StatusText { get; set; }
}