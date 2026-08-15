using System;
using System.Windows;
using System.Windows.Controls;
using ApertureNeo.Controls.FolderTree;
using ApertureNeo.Services;
using ApertureNeo.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace ApertureNeo.Views;

/// <summary>
/// Folder-tree panel: hosts <see cref="FolderTreeView"/> + the
/// "up" / "back" / "forward" / "home" floating chips. The inline
/// ItemTemplate + ItemContainerStyle that used to live in
/// MainWindow.xaml (62 lines) is now owned by this view —
/// that's the biggest single XAML chunk that moved out.
///
/// P2: binds to <see cref="FolderTreePanelViewModel"/> via
/// DataContext. The "up" / "back" / "forward" / "home" buttons
/// bind to the VM's UpCommand / BackCommand / ForwardCommand /
/// ReturnToRootCommand; their Visibility toggles on drill mode +
/// history availability via bindings to
/// <see cref="FolderTreePanelViewModel.IsDrillMode"/>,
/// <see cref="FolderTreePanelViewModel.CanGoBack"/> and
/// <see cref="FolderTreePanelViewModel.CanGoForward"/>.
///
/// The View is the bridge between the tree control (which the
/// VM doesn't have a reference to) and the VM: it forwards the
/// tree's events (DrillModeChanged, FolderSelected) into VM
/// setters, mirrors the NavigationService browse-history flags
/// into the VM (Round Z — the history no longer lives in the
/// tree), and translates the VM's Up / ReturnToRoot events into
/// <see cref="FolderTreeView"/> method calls. Back / Forward are
/// handled by MainWindow, which owns the NavigationService cursor
/// moves + the tree re-drill (the panel View can't reach the
/// navigation service's <see cref="INavigationService.GoBack"/>
/// without knowing the target for the re-drill).
/// </summary>
public partial class FolderTreePanelView : UserControl
{
    public FolderTreePanelView()
    {
        InitializeComponent();
        DataContext = AppHost.Services?.GetService<FolderTreePanelViewModel>();

        // P2: the VM owns IsDrillMode (used by the chip
        // Visibility bindings). The tree doesn't raise
        // PropertyChanged for IsInDrillMode — only the
        // custom DrillModeChanged event — so the View
        // forwards the event into a VM setter.
        FolderTree.DrillModeChanged += () =>
        {
            if (DataContext is FolderTreePanelViewModel vm)
                vm.SetDrillMode(FolderTree.IsInDrillMode);
        };

        // Round Z: the back/forward chips are driven by the
        // NavigationService browse history (visit list + cursor),
        // not by the tree. Mirror HistoryChanged into the VM so
        // the chip Visibility bindings refresh, and seed once at
        // construction (the initial XAML bindings run before any
        // history exists, and the service may already have
        // entries if this view is rebuilt mid-session, e.g. on a
        // theme switch).
        var nav = AppHost.Services?.GetService<INavigationService>();
        if (nav != null)
        {
            nav.HistoryChanged += () =>
            {
                if (DataContext is FolderTreePanelViewModel vm)
                {
                    vm.SetCanGoBack(nav.CanGoBack);
                    vm.SetCanGoForward(nav.CanGoForward);
                }
            };
            if (DataContext is FolderTreePanelViewModel vm3)
            {
                vm3.SetCanGoBack(nav.CanGoBack);
                vm3.SetCanGoForward(nav.CanGoForward);
            }
        }

        // P2: the VM owns the folder-selection side effects
        // (recent-add + FolderNavigationRequested event).
        // The View forwards the tree's selection event into
        // the VM; MainWindow subscribes to FolderNavigation
        // Requested and calls NavigationService.LoadFolder
        // (with the browse-history flag the tree attached).
        FolderTree.FolderSelected += (source, path, recordHistory, trackRecent) =>
        {
            if (DataContext is FolderTreePanelViewModel vm)
                vm.OnFolderSelected(source, path, recordHistory, trackRecent);
        };

        // P2 / Round X / Round Z: Up / ReturnToRoot commands on
        // the VM need to reach FolderTree (a UI control, not a
        // DI service). The View translates the VM's events into
        // the tree's method calls. This replaces the routing
        // that used to live in MainWindow's WireTreePanelEvents
        // + TreeController. Note the previous
        // BackRequested→NavigateBack wiring moved to
        // UpRequested→NavigateBack (which now strictly means
        // "up one directory level"); back/forward are routed by
        // MainWindow (NavigationService.GoBack/GoForward + a
        // tree re-drill via ReDrillToPath) since the cursor
        // lives in the service.
        if (DataContext is FolderTreePanelViewModel vm2)
        {
            vm2.UpRequested += (_, _) => FolderTree.NavigateBack();
            vm2.ReturnToRootRequested += (_, _) => FolderTree.ReturnToRoot();
        }
    }

    public Border TreePanelRef => TreePanel;
    public FolderTreeView FolderTreeRef => FolderTree;
    public Button BtnTreeUpRef => BtnTreeUp;
    public Button BtnTreeBackRef => BtnTreeBack;
    public Button BtnTreeForwardRef => BtnTreeForward;
    public Button BtnReturnToRootRef => BtnReturnToRoot;
}