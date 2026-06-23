using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ApertureNeo.Controls.FolderTree;
using ApertureNeo.Helpers;
using ApertureNeo.Services;

namespace ApertureNeo.ViewModels;

/// <summary>
/// State + commands for the folder-tree panel
/// (<see cref="ApertureNeo.Views.FolderTreePanelView"/>).
///
/// The actual <see cref="FolderTreeView"/> is a complex
/// ItemsControl that manages its own Favorites / Recent /
/// ThisPC sections + drill state. The VM is thin here: it
/// surfaces the tree's existing events as commands for the
/// host (Back / ReturnToRoot) and re-publishes the drill-mode
/// flag for the floating-chips visibility.
///
/// P2: folder-selection side effects (recording a recent
/// visit) now live here rather than in MainWindow's
/// controllers. The View forwards the tree's
/// <see cref="FolderTreeView.FolderSelected"/> event to
/// <see cref="OnFolderSelected"/>; this VM adds the folder
/// to the recent list (when it isn't a Favorites / Recent
/// internal click) and raises
/// <see cref="FolderNavigationRequested"/> for the host to
/// load.
///
/// P2: the back / return-to-root commands stay as VM
/// commands (bound by XAML on the chips); the View
/// translates them into <see cref="FolderTreeView"/> method
/// calls (the View is the only thing that can reach the
/// tree control). The drill-chip visibility now binds
/// directly to <see cref="IsDrillMode"/> via the global
/// BoolToVisibility converter, so MainWindow no longer
/// needs to forward DrillModeChanged.
/// </summary>
public partial class FolderTreePanelViewModel : ObservableObject
{
    private readonly ISettingsStore _settings;

    public FolderTreePanelViewModel(ISettingsStore settings)
    {
        _settings = settings;
    }

    [ObservableProperty]
    private bool _isDrillMode;

    /// <summary>Called by the View in response to
    /// <see cref="FolderTreeView.DrillModeChanged"/>. Updates
    /// <see cref="IsDrillMode"/> so the chip Visibility
    /// bindings refresh.</summary>
    public void SetDrillMode(bool inDrill) => IsDrillMode = inDrill;

    /// <summary>Called by the View in response to
    /// <see cref="FolderTreeView.FolderSelected"/>. Records
    /// the folder as a recent visit (unless the click was on
    /// the Favorites / Recent section, which would be
    /// circular) and raises
    /// <see cref="FolderNavigationRequested"/> for the host
    /// to call <c>NavigationService.LoadFolder</c>.</summary>
    public void OnFolderSelected(FolderSource source, string path)
    {
        if (source != FolderSource.Favorite && source != FolderSource.Recent
            && FormatHelper.FolderHasImages(path))
        {
            _settings.AddRecent(path);
        }
        FolderNavigationRequested?.Invoke(this, path);
    }

    /// <summary>Raised after <see cref="OnFolderSelected"/>
    /// accepts a folder. The host (MainWindow) subscribes and
    /// calls <c>NavigationService.LoadFolder(path)</c>.</summary>
    public event EventHandler<string>? FolderNavigationRequested;

    [RelayCommand]
    private void Back() => BackRequested?.Invoke(this, EventArgs.Empty);
    public event EventHandler? BackRequested;

    [RelayCommand]
    private void ReturnToRoot() => ReturnToRootRequested?.Invoke(this, EventArgs.Empty);
    public event EventHandler? ReturnToRootRequested;
}