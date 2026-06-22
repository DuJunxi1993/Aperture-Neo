using System;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using ApertureNeo.Helpers;
using ApertureNeo.Services;

namespace ApertureNeo.ViewModels;

/// <summary>
/// State + commands for <see cref="ApertureNeo.Views.TitleBarView"/>.
/// Owns the "打开" / "toggle tree" / "toggle thumb" / "clear
/// cache" / "clear recent" / "about" commands plus the
/// IsUpdateAvailable flag (drives the "（有版本更新）" suffix
/// on the 关于 menu item).
///
/// Cross-window coordination flows through <see cref="IUiState"/>:
/// toggle-tree flips <see cref="IUiState.IsTreeVisible"/>,
/// which MainWindow's column-visibility logic observes. No
/// events back to the view; the VM just mutates shared state.
///
/// DI scope: Transient (per shell / per MainWindow).
/// </summary>
public partial class TitleBarViewModel : ObservableObject
{
    private readonly INavigationService _navigation;
    private readonly ISettingsStore _settingsStore;
    private readonly IUiState _uiState;
    private readonly IThumbnailCache _cache;

    public TitleBarViewModel(
        INavigationService navigation,
        ISettingsStore settingsStore,
        IUiState uiState,
        IThumbnailCache cache)
    {
        _navigation = navigation;
        _settingsStore = settingsStore;
        _uiState = uiState;
        _cache = cache;
    }

    /// <summary>Bound to the "（有版本更新）" suffix Visibility in TitleBarView.
    /// Set by MainWindow when the About window's update check
    /// reports a new release.</summary>
    public bool IsUpdateAvailable
    {
        get => _isUpdateAvailable;
        set => SetProperty(ref _isUpdateAvailable, value);
    }
    private bool _isUpdateAvailable;

    [RelayCommand]
    private void Open()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = FormatHelper.Filter,
            Multiselect = false,
            RestoreDirectory = true,
        };
        if (dialog.ShowDialog() != true) return;
        var folder = Path.GetDirectoryName(dialog.FileName);
        if (string.IsNullOrEmpty(folder)) return;
        if (FormatHelper.FolderHasImages(folder))
            _settingsStore.AddRecent(folder);
        _navigation.LoadFolder(folder);
        _navigation.NavigateTo(dialog.FileName);
    }

    [RelayCommand]
    private void ToggleTreeColumn()
    {
        // Flip the shared flag. MainWindow observes
        // IUiState.PropertyChanged and applies the column
        // width change.
        _uiState.IsTreeVisible = !_uiState.IsTreeVisible;
    }

    [RelayCommand]
    private void ToggleThumbColumn()
    {
        _uiState.IsThumbVisible = !_uiState.IsThumbVisible;
    }

    [RelayCommand]
    private async Task ClearThumbnailCacheAsync()
    {
        try
        {
            await _cache.ClearAsync();
            // Clear the in-memory thumbnail bytes on every item
            // so the UI re-decodes on next paint. The Thumbnail
            // property is just a wrapper around the cached bytes;
            // setting it to null forces the thumb panel to
            // re-render with a placeholder.
            foreach (var item in _navigation.Items)
            {
                item.Thumbnail = null;
                item.HasThumbnailError = false;
                item.ThumbnailErrorMessage = null;
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write("Cache", "clear fail", ex);
        }
    }

    [RelayCommand]
    private void ClearRecent()
    {
        _settingsStore.ClearRecent();
    }

    [RelayCommand]
    private void OpenAbout()
    {
        OpenAboutRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Raised when the user picks the 关于 menu item. The
    /// host MainWindow opens AboutWindow and subscribes to its
    /// UpdateAvailableChanged event to drive IsUpdateAvailable.
    /// Kept as an event (not a service call) because the host
    /// owns the lifetime + modal-ShowDialog flow for the
    /// secondary window.</summary>
    public event EventHandler? OpenAboutRequested;
}