using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace ApertureNeo;

/// <summary>
/// Folder-tree concerns: the "back" / "return to root" floating
/// chips above the tree (shown only in drill mode) and the
/// floating tree popup that appears when the user mouses over
/// the 8px left-edge hot zone while the inline tree is
/// collapsed. Extracted from MainWindow.xaml.cs as a partial
/// class — all methods touch only the fields and XAML
/// elements the partial class already shares.
/// </summary>
public partial class MainWindow
{
    private void BtnReturnToRoot_Click(object sender, RoutedEventArgs e)
    {
        FolderTree.ReturnToRoot();
    }

    /// <summary>
    /// Floating "back" chip shown above the tree when the user has
    /// drilled into a folder. Pops one level off the navigation
    /// stack — replaces the deprecated BackNode tree entry that
    /// used to appear at the top of the tree list. (Earlier this
    /// incorrectly called <see cref="FolderTreeView.ReturnToRoot"/>,
    /// which collapsed the entire drill stack in one click; the
    /// "back one level" semantics require NavigateBack, which is
    /// now public for exactly this purpose.)
    /// </summary>
    private void BtnTreeBack_Click(object sender, RoutedEventArgs e)
    {
        FolderTree.NavigateBack();
    }

    private void UpdateReturnToRootVisibility()
    {
        // Both floating chips are only visible while in drill mode.
        // Out of drill mode neither makes sense (there's nothing to
        // back out of, and the user is already at the section
        // overview). The two chips cover "one level up" and "all the
        // way back", respectively.
        var inDrill = FolderTree.IsInDrillMode;
        BtnTreeBack.Visibility = inDrill ? Visibility.Visible : Visibility.Collapsed;
        BtnReturnToRoot.Visibility = inDrill ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---- Floating tree popup (hot zone + panel) ----

    private DispatcherTimer? _treeHideTimer;

    /// <summary>
    /// Mouse entered the 8px left-edge hot zone. Show the floating
    /// tree popup if the inline tree is currently collapsed. The
    /// popup mirrors the inline tree's items and selected node via
    /// SyncFrom, so the visual state is consistent on first show.
    /// </summary>
    private void TreeHotZone_MouseEnter(object sender, MouseEventArgs e)
    {
        if (_isFullscreen || _isTreeVisible) return;
        _treeHideTimer?.Stop();
        if (!TreeFloatingPopup.IsOpen)
        {
            FolderTreeFloating.SyncFrom(FolderTree);
            TreeFloatingPopup.HorizontalOffset = 0;
            TreeFloatingPopup.VerticalOffset = 0;
            TreeFloatingPopup.IsOpen = true;
        }
    }

    private void TreeHotZone_MouseLeave(object sender, MouseEventArgs e)
    {
        // Don't close the popup yet — the user is probably moving
        // their cursor INTO the floating tree. The popup's own
        // MouseLeave handler will fire when the cursor truly exits
        // both the hot zone and the popup, at which point we close.
    }

    private void TreeFloatingPanel_MouseEnter(object sender, MouseEventArgs e)
    {
        _treeHideTimer?.Stop();
    }

    /// <summary>
    /// Mouse left the floating panel. Start a short hide timer so
    /// the panel doesn't close while the cursor is crossing the
    /// 4px gap between the popup edge and the next target. If the
    /// cursor re-enters within 250ms (via hot zone or panel), the
    /// timer is cancelled.
    /// </summary>
    private void TreeFloatingPanel_MouseLeave(object sender, MouseEventArgs e)
    {
        if (_treeHideTimer == null)
        {
            _treeHideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _treeHideTimer.Tick += (_, _) =>
            {
                _treeHideTimer.Stop();
                TreeFloatingPopup.IsOpen = false;
            };
        }
        _treeHideTimer.Stop();
        _treeHideTimer.Start();
    }

    private void TreeFloatingPopup_Closed(object? sender, EventArgs e)
    {
        // The popup closed (either via the hide timer or by the
        // user collapsing the tree). Nothing to do beyond a
        // defensive timer stop.
        _treeHideTimer?.Stop();
    }
}
