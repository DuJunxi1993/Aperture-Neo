using System;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using ApertureNeo.Services;

namespace ApertureNeo.ViewModels;

/// <summary>
/// State + commands for the folder-tree panel
/// (<see cref="ApertureNeo.Views.FolderTreePanelView"/>).
///
/// The actual <see cref="Controls.FolderTree.FolderTreeView"/>
/// is a complex ItemsControl that manages its own Favorites /
/// Recent / ThisPC sections + drill state. The VM is thin
/// here: it surfaces the tree's existing events as commands
/// for the host (Back / ReturnToRoot) and re-exposes the
/// drill-mode flag for the floating-chips visibility.
///
/// The view binds to the tree's own events (FolderSelected /
/// DrillModeChanged) directly; the VM re-publishes them as
/// .NET events for the host to subscribe to. Cross-window
/// state (column visibility) flows through the tree's
/// existing IsInDrillMode API.
/// </summary>
public partial class FolderTreePanelViewModel : ObservableObject
{
    public FolderTreePanelViewModel()
    {
    }

    [RelayCommand]
    private void Back() => BackRequested?.Invoke(this, EventArgs.Empty);
    public event EventHandler? BackRequested;

    [RelayCommand]
    private void ReturnToRoot() => ReturnToRootRequested?.Invoke(this, EventArgs.Empty);
    public event EventHandler? ReturnToRootRequested;
}