using System;
using System.Windows;
using System.Windows.Controls;
using ApertureNeo.Controls.FolderTree;

namespace ApertureNeo.Views;

/// <summary>
/// Folder-tree panel: hosts <see cref="FolderTreeView"/> + the
/// "back" / "return to root" floating chips (drill mode only).
/// The inline ItemTemplate + ItemContainerStyle that used to
/// live in MainWindow.xaml (62 lines) is now owned by this
/// view — that's the biggest single XAML chunk that moved out.
///
/// The host (MainWindow) reads <see cref="FolderTreeRef"/> to
/// wire events (FolderSelected, DrillModeChanged) and drives
/// the chip visibility via <see cref="UpdateReturnToRootVisibility"/>
/// (called when the tree's drill state changes).
/// </summary>
public partial class FolderTreePanelView : UserControl
{
    public FolderTreePanelView()
    {
        InitializeComponent();
    }

    public Border TreePanelRef => TreePanel;
    public FolderTreeView FolderTreeRef => FolderTree;
    public Button BtnTreeBackRef => BtnTreeBack;
    public Button BtnReturnToRootRef => BtnReturnToRoot;

    public event EventHandler? BackRequested;
    public event EventHandler? ReturnToRootRequested;

    /// <summary>Toggle the floating chips' visibility based on
    /// whether the tree is currently in drill mode.</summary>
    public void UpdateReturnToRootVisibility(bool inDrill)
    {
        BtnTreeBack.Visibility = inDrill ? Visibility.Visible : Visibility.Collapsed;
        BtnReturnToRoot.Visibility = inDrill ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BtnTreeBack_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);
    private void BtnReturnToRoot_Click(object sender, RoutedEventArgs e) => ReturnToRootRequested?.Invoke(this, EventArgs.Empty);
}