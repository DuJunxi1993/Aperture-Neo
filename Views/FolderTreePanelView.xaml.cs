using System;
using System.Windows;
using System.Windows.Controls;
using ApertureNeo.Controls.FolderTree;
using ApertureNeo.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace ApertureNeo.Views;

/// <summary>
/// Folder-tree panel: hosts <see cref="FolderTreeView"/> + the
/// "back" / "return to root" floating chips (drill mode only).
/// The inline ItemTemplate + ItemContainerStyle that used to
/// live in MainWindow.xaml (62 lines) is now owned by this
/// view — that's the biggest single XAML chunk that moved out.
///
/// P2: binds to <see cref="FolderTreePanelViewModel"/> via
/// DataContext. The "back" / "return to root" buttons bind to
/// the VM's BackCommand / ReturnToRootCommand; their Visibility
/// toggles on drill mode via a binding to
/// <see cref="FolderTreePanelViewModel.IsDrillMode"/>.
///
/// The View is the bridge between the tree control (which the
/// VM doesn't have a reference to) and the VM: it forwards the
/// tree's events (DrillModeChanged, FolderSelected) into VM
/// setters, and the VM's Back / ReturnToRoot events into
/// <see cref="FolderTreeView"/> method calls.
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

        // P2: the VM owns the folder-selection side effects
        // (recent-add + FolderNavigationRequested event).
        // The View forwards the tree's selection event into
        // the VM; MainWindow subscribes to FolderNavigationRequested
        // and calls NavigationService.LoadFolder.
        FolderTree.FolderSelected += (source, path) =>
        {
            if (DataContext is FolderTreePanelViewModel vm)
                vm.OnFolderSelected(source, path);
        };

        // P2: Back / ReturnToRoot commands on the VM need to
        // reach FolderTree (a UI control, not a DI service).
        // The View translates the VM's events into the tree's
        // method calls. This replaces the routing that used to
        // live in MainWindow's WireTreePanelEvents + TreeController.
        if (DataContext is FolderTreePanelViewModel vm2)
        {
            vm2.BackRequested += (_, _) => FolderTree.NavigateBack();
            vm2.ReturnToRootRequested += (_, _) => FolderTree.ReturnToRoot();
        }
    }

    public Border TreePanelRef => TreePanel;
    public FolderTreeView FolderTreeRef => FolderTree;
    public Button BtnTreeBackRef => BtnTreeBack;
    public Button BtnReturnToRootRef => BtnReturnToRoot;
}