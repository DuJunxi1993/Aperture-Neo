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
/// host (Up / Back / Forward / ReturnToRoot) and re-publishes
/// the drill-mode + history-available flags for the floating
/// chips' visibility / enabled state.
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
///
/// Round X / Y / Z (back-forward split): the browse history
/// lives in <c>NavigationService</c> (visit list + cursor,
/// Explorer-style), not in the tree — so this VM's
/// <see cref="CanGoBack"/> / <see cref="CanGoForward"/> are
/// fed by <c>INavigationService.HistoryChanged</c> via
/// <see cref="SetCanGoBack"/> / <see cref="SetCanGoForward"/>,
/// and the Back/Forward commands are handled by MainWindow
/// (<c>GoBack()</c>/<c>GoForward()</c> + a tree re-drill);
/// only Up and ReturnToRoot still reach the tree directly
/// through the View. Commands:
/// <list type="bullet">
///   <item><see cref="UpCommand"/> → "up one directory level"
///         (tree-stack pop, restores parent view + parent
///         folder's images; recorded as a fresh visit so Back
///         returns to the child).</item>
/// <item><see cref="BackCommand"/> → "back to previous
    ///         browsing location" (cursor move in NavigationService;
    ///         the tree is re-drilled so the restored folder is
    ///         selected).</item>
    ///   <item><see cref="ForwardCommand"/> → "forward to the
    ///         browsing location retraced by Back" (cursor move,
    ///         mirrors Back).</item>
    ///   <item><see cref="ReturnToRootCommand"/> → show the home
    ///         view (drill stack reset; the browse history is
    ///         kept, matching Explorer).</item>
/// </list>
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

    /// <summary>True when the browse-history cursor is not at the
    /// first visit (there is a folder to go back to). Mirrored
    /// from <c>NavigationService</c> via
    /// <see cref="SetCanGoBack"/>; the back chip binds to it so
    /// the button hides itself at session start when there's
    /// nowhere to go.</summary>
    [ObservableProperty]
    private bool _canGoBack;

    /// <summary>True when a Back step can be retraced (cursor is
    /// not at the last visit). Mirrored from
    /// <c>NavigationService</c> via
    /// <see cref="SetCanGoForward"/>; the forward chip binds to
    /// it so the button only appears after a Back step.</summary>
    [ObservableProperty]
    private bool _canGoForward;

    /// <summary>Called by the View in response to
    /// <see cref="FolderTreeView.DrillModeChanged"/>. Updates
    /// <see cref="IsDrillMode"/> so the chip Visibility
    /// bindings refresh.</summary>
    public void SetDrillMode(bool inDrill) => IsDrillMode = inDrill;

    /// <summary>Called by the View in response to
    /// <c>INavigationService.HistoryChanged</c>. Updates
    /// <see cref="CanGoBack"/> so the back chip's Visibility
    /// binding refreshes.</summary>
    public void SetCanGoBack(bool canGoBack) => CanGoBack = canGoBack;

    /// <summary>Called by the View in response to
    /// <c>INavigationService.HistoryChanged</c>. Updates
    /// <see cref="CanGoForward"/> so the forward chip's
    /// Visibility binding refreshes.</summary>
    public void SetCanGoForward(bool canGoForward) => CanGoForward = canGoForward;

    /// <summary>Called by the View in response to
    /// <see cref="FolderTreeView.FolderSelected"/>. Records
    /// the folder as a recent visit (only when
    /// <paramref name="trackRecent"/> — genuine user navigations
    /// like mouse clicks / hotkeys / jumps; Up, Back and Forward
    /// restores are excluded so the recent list and the home
    /// highlight reflect only what the user chose to browse) and
    /// raises <see cref="FolderNavigationRequested"/> for the host
    /// to call <c>NavigationService.LoadFolder(path,
    /// recordHistory)</c> — the flag decides whether the folder
    /// enters the browse history (Round Z: user-initiated
    /// navigations record, automatic restores don't).</summary>
    public void OnFolderSelected(FolderSource source, string path, bool recordHistory, bool trackRecent)
    {
        if (trackRecent && source != FolderSource.Favorite && source != FolderSource.Recent
            && FormatHelper.FolderHasImages(path))
        {
            _settings.AddRecent(path);
        }
        FolderNavigationRequested?.Invoke(this, new FolderNavigationRequest(path, recordHistory));
    }

    /// <summary>Raised after <see cref="OnFolderSelected"/>
    /// accepts a folder. The host (MainWindow) subscribes and
    /// calls <c>NavigationService.LoadFolder</c> with the
    /// carried <see cref="FolderNavigationRequest.RecordHistory"/>
    /// flag.</summary>
    public event EventHandler<FolderNavigationRequest>? FolderNavigationRequested;

    /// <summary>
    /// Payload for <see cref="FolderNavigationRequested"/>: the
    /// folder to load plus the browse-history flag the tree decided
    /// (user navigation records, automatic restore doesn't).
    /// </summary>
    public sealed record FolderNavigationRequest(string Path, bool RecordHistory);

    [RelayCommand]
    private void Up() => UpRequested?.Invoke(this, EventArgs.Empty);
    public event EventHandler? UpRequested;

    [RelayCommand]
    private void Back() => BackRequested?.Invoke(this, EventArgs.Empty);
    public event EventHandler? BackRequested;

    [RelayCommand]
    private void Forward() => ForwardRequested?.Invoke(this, EventArgs.Empty);
    public event EventHandler? ForwardRequested;

    [RelayCommand]
    private void ReturnToRoot() => ReturnToRootRequested?.Invoke(this, EventArgs.Empty);
    public event EventHandler? ReturnToRootRequested;
}