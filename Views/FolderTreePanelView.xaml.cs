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
/// DataContext. The "back" / "return to root" buttons bind
/// to the VM's BackCommand / ReturnToRootCommand. The
/// chip visibility toggling (drill-mode-only) is now driven
/// via XAML DataTrigger on FolderTree.IsInDrillMode (a public
/// property on the tree control itself).
/// </summary>
public partial class FolderTreePanelView : UserControl
{
    public FolderTreePanelView()
    {
        InitializeComponent();
        DataContext = AppHost.Services?.GetService<FolderTreePanelViewModel>();
    }

    public Border TreePanelRef => TreePanel;
    public FolderTreeView FolderTreeRef => FolderTree;
    public Button BtnTreeBackRef => BtnTreeBack;
    public Button BtnReturnToRootRef => BtnReturnToRoot;
}