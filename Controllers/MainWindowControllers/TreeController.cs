using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace ApertureNeo;

/// <summary>
/// Folder-tree concerns specific to the floating tree popup
/// that appears when the user mouses over the 8px left-edge
/// hot zone while the inline tree is collapsed.
///
/// P2 cleanup: removed the drill-chip visibility logic
/// (UpdateReturnToRootVisibility + BtnReturnToRoot_Click +
/// BtnTreeBack_Click). FolderTreePanelView now binds the
/// chips' Visibility to FolderTreePanelVM.IsDrillMode via
/// the global BoolToVisibility converter, and the VM's
/// BackCommand / ReturnToRootCommand are translated into
/// <see cref="FolderTreeView"/> method calls inside the
/// view's own code-behind (the tree control isn't reachable
/// from the VM).
/// </summary>
public partial class MainWindow
{
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