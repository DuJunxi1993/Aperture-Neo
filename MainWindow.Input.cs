using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ApertureNeo.Helpers;
using ApertureNeo.ViewModels;

namespace ApertureNeo;

/// <summary>
/// Window-level input handling + popover positioning:
/// drag-and-drop file open, keyboard dispatch (Ctrl+F / Ctrl+O
/// / Esc / F5 / arrows / PageUp/PageDown), thumbnail-grid
/// arrow-key navigation (row/col aware), and the info popover's
/// click-outside-to-dismiss + right-align-with-pill logic.
///
/// P2 step 9: this was previously split across two controller
/// partials (ChromeController + InfoPopoverController) under
/// Controllers/MainWindowControllers/. Those controllers are
/// deleted; their remaining live methods are consolidated here.
///
/// These stay in MainWindow because they manipulate the
/// window's visual tree directly (preview-tunneling click
/// events, popup positioning from the pill rect) or run
/// OS-level keyboard handling that the VMs can't reach.
/// </summary>
public partial class MainWindow
{
    // ---- Drag-and-drop ----

    private void OnWindowDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var files = (string[])e.Data.GetData(DataFormats.FileDrop);
        if (files == null || files.Length == 0) return;
        var file = files[0];
        if (!File.Exists(file) || !FormatHelper.IsSupported(file)) return;
        var folder = Path.GetDirectoryName(file);
        if (string.IsNullOrEmpty(folder)) return;
        // P1 fix: if the dropped file's folder has no other supported
        // images, skip the load entirely. The previous code always
        // called LoadFolder + NavigateTo even for an empty folder,
        // which left the viewer in an inconsistent state: the folder
        // was empty (so the thumbnail panel + empty-state overlay
        // showed), but _currentFolder was set + AddRecent was skipped.
        // Bailing out keeps state consistent. (The user can always
        // drop a different file.)
        if (!FormatHelper.FolderHasImages(folder)) return;
        _settings.AddRecent(folder);
        _navigation.LoadFolder(folder);
        _navigation.NavigateTo(file);
    }

    // ---- Keyboard ----

    private bool HandleKey(Key key)
    {
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) return false;

        if (ctrl)
        {
            switch (key)
            {
                case Key.F:
                    // P2: route through the fullscreen controller.
                    // Toggle handles the pre-toggle WindowState
                    // capture internally (on the entering
                    // direction only), so the exit press can't
                    // re-capture the in-fullscreen Maximized
                    // state and trick the exit transition into
                    // re-maximizing a window that was Normal.
                    _fullscreen.Toggle();
                    return true;
                case Key.O:
                    if (TitleBar.DataContext is TitleBarViewModel titleVmForOpen)
                        titleVmForOpen.OpenCommand.Execute(null);
                    return true;
                case Key.OemPlus: case Key.Add: ImageViewer.ZoomIn(); return true;
                case Key.OemMinus: case Key.Subtract: ImageViewer.ZoomOut(); return true;
                case Key.D0: ImageViewer.FitToScreen(); return true;
                case Key.Left: case Key.Right: return LinearNavigate(key);
            }
        }

        if (TryGridNavigate(key)) return true;

        switch (key)
        {
            case Key.Left: case Key.Up: return LinearNavigate(Key.Up);
            case Key.Right: case Key.Down: return LinearNavigate(Key.Down);
            case Key.Escape:
                if (_fullscreen.IsFullscreen) { _fullscreen.Toggle(); return true; }
                else if (_slideshow.IsRunning) { _slideshow.Stop(); return true; }
                return false;
            case Key.F5: _slideshow.Toggle(); return true;
            case Key.PageUp:
                FolderTree.NavigateToAdjacentFolder(_navigation.CurrentFolder, forward: false);
                return true;
            case Key.PageDown:
                FolderTree.NavigateToAdjacentFolder(_navigation.CurrentFolder, forward: true);
                return true;
        }
        return false;
    }

    private bool LinearNavigate(Key key)
    {
        if (key == Key.Up || key == Key.Left) { _navigation.MovePrevious(); return true; }
        if (key == Key.Down || key == Key.Right) { _navigation.MoveNext(); return true; }
        return false;
    }

    private int GetThumbnailColumnCount()
    {
        double width = ThumbColumn.ActualWidth;
        if (width <= 0) return 1;
        return Math.Max(1, (int)(width / 156.0));
    }

    private bool TryGridNavigate(Key key)
    {
        if (_fullscreen.IsFullscreen) return false;
        if (!_isThumbVisible) return false;
        int cols = GetThumbnailColumnCount();
        if (cols < 2) return false;
        int total = _navigation.Count;
        if (total <= 0) return false;
        int currentIdx = _navigation.CurrentIndex;
        if (currentIdx < 0) return false;

        int row = currentIdx / cols, col = currentIdx % cols;
        int lastRow = (total - 1) / cols, targetIdx = -1;

        switch (key)
        {
            // Left: prefer same-row predecessor; if at column 0, wrap up to
            //       the previous row's LAST column (visually: from the left
            //       edge of row N, move to the right edge of row N-1).
            // Right: prefer same-row successor; if at last column, wrap down
            //        to the next row's FIRST column (visually: from the right
            //        edge of row N, move to the left edge of row N+1).
            case Key.Left:
                if (col > 0) targetIdx = currentIdx - 1;
                else if (row > 0) targetIdx = currentIdx - (col + 1);
                break;
            case Key.Right:
                if (col < cols - 1 && currentIdx + 1 < total) targetIdx = currentIdx + 1;
                else if (row < lastRow) targetIdx = currentIdx + (cols - col);
                break;
            case Key.Up: if (row > 0) targetIdx = currentIdx - cols; break;
            case Key.Down: if (row < lastRow) targetIdx = Math.Min(currentIdx + cols, total - 1); break;
            default: return false;
        }

        if (targetIdx >= 0 && targetIdx < total) { _navigation.MoveTo(targetIdx); return true; }
        return false;
    }

    // ---- Chrome visibility (column widths + overlay show/hide) ----

    /// <summary>
    /// Show/hide the persistent overlay chrome based on fullscreen state.
    /// P1: fullscreen state is read from the controller (which
    /// owns the state machine + the transition). The edge-nav
    /// cleanup is dispatched to the controller too — calling its
    /// internal HideEdgeNav via the field's properties would
    /// require making them internal-visible, so we just hide the
    /// UserControls here (the controller's _edgeNavVisible state
    /// is left alone; the next ShowEdgeNav re-syncs via
    /// UpdateSelection-style logic).
    ///
    /// Non-fullscreen: TitleBar, FloatingBar, InfoPill all Visible; edge
    /// nav buttons Collapsed (they are fullscreen-only).
    ///
    /// Fullscreen: TitleBar, FloatingBar, InfoPill all Collapsed
    /// IMMEDIATELY (no mouse-event triggers show them again — that's
    /// deliberate, per the Linear design). Edge nav buttons stay hidden
    /// too until the user moves the mouse, at which point ShowEdgeNav
    /// handles them on a 3s timer.
    /// </summary>
    private void UpdateOverlayVisibility()
    {
        if (_fullscreen.IsFullscreen)
        {
            TitleBarArea.Visibility = Visibility.Collapsed;
            TitleBarRow.Height = new GridLength(0);
            FloatingBarContent.Visibility = Visibility.Collapsed;
            InfoPillContent.Visibility = Visibility.Collapsed;
            // Edge nav is left in whatever state it was — entering
            // fullscreen shows it via ShowEdgeNav (on the
            // controller), and subsequent mouse moves will
            // reset its 3s timer.
        }
        else
        {
            TitleBarArea.Visibility = Visibility.Visible;
            TitleBarRow.Height = new GridLength(44);
            FloatingBarContent.Visibility = Visibility.Visible;
            InfoPillContent.Visibility = Visibility.Visible;
            // Leaving fullscreen: hide the edge nav UserControls
            // so they don't show over the restored chrome. The
            // controller's _edgeNavVisible is set false on the
            // next Toggle (which happens via the same IUiState
            // PropertyChanged branch that called this).
            EdgeNavLeftControl.Visibility = Visibility.Collapsed;
            EdgeNavRightControl.Visibility = Visibility.Collapsed;
            EdgeNavLeftControl.Opacity = 0;
            EdgeNavRightControl.Opacity = 0;
        }
    }

    private GridLength _savedTreeWidth = new(232);
    private GridLength _savedThumbWidth = new(400);

    private void ApplyColumnVisibility()
    {
        if (_fullscreen.IsFullscreen)
        {
            // Save current widths before zeroing so exiting fullscreen
            // restores the user's pre-fullscreen column proportions.
            _savedTreeWidth = TreeColumn.Width;
            _savedThumbWidth = ThumbColumn.Width;
            TreeColumn.Width = new GridLength(0);      TreeColumn.MinWidth = 0;
            TreeSplitterColumn.Width = new GridLength(0);
            TreeSplitter.Visibility = Visibility.Collapsed;
            ThumbColumn.Width = new GridLength(0);     ThumbColumn.MinWidth = 0;
            ThumbSplitterColumn.Width = new GridLength(0);
            ThumbSplitter.Visibility = Visibility.Collapsed;
            TreeHotZone.Visibility = Visibility.Collapsed;
            if (TreeFloatingPopup.IsOpen) TreeFloatingPopup.IsOpen = false;
            return;
        }

        if (_isTreeVisible)
        {
            TreeColumn.Width = _savedTreeWidth;
        }
        else
        {
            if (TreeColumn.Width.Value > 0) _savedTreeWidth = TreeColumn.Width;
            TreeColumn.Width = new GridLength(0);
        }
        TreeColumn.MinWidth = _isTreeVisible ? 180 : 0;
        TreeSplitterColumn.Width = _isTreeVisible ? new GridLength(1, GridUnitType.Auto) : new GridLength(0);
        TreeSplitter.Visibility = _isTreeVisible ? Visibility.Visible : Visibility.Collapsed;
        if (_isThumbVisible)
        {
            ThumbColumn.Width = _savedThumbWidth;
        }
        else
        {
            if (ThumbColumn.Width.Value > 0) _savedThumbWidth = ThumbColumn.Width;
            ThumbColumn.Width = new GridLength(0);
        }
        ThumbColumn.MinWidth = _isThumbVisible ? 160 : 0;
        ThumbSplitterColumn.Width = _isThumbVisible ? new GridLength(6) : new GridLength(0);
        ThumbSplitter.Visibility = _isThumbVisible ? Visibility.Visible : Visibility.Collapsed;
        TreeHotZone.Visibility = _isTreeVisible ? Visibility.Collapsed : Visibility.Visible;
        if (_isTreeVisible) TreeFloatingPopup.IsOpen = false;
    }

    // ---- Info popover (click-outside + right-align-with-pill) ----

    /// <summary>
    /// R70: close the info popover when the user clicks anywhere
    /// outside both the InfoPill and the popover itself. We hook
    /// PreviewMouseLeftButtonDown at the window level (tunneling)
    /// so we see every click before the popup's own auto-close
    /// logic runs. Clicks on the pill itself are ignored here
    /// because the pill's MouseLeftButtonUp toggles
    /// the popover; clicks on the popover's body are ignored
    /// because we want the user to be able to select text inside
    /// the popover (e.g. the file name) without dismissing it.
    /// </summary>
    private void OnWindowPreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!InfoPopover.IsOpen) return;
        var src = e.OriginalSource as DependencyObject;
        // Click on the pill: the pill's MouseLeftButtonUp toggles.
        if (VisualTreeHelpers.IsDescendantOf(src, InfoPillContent)) return;
        // Click inside the popover's content: keep open so the
        // user can select / interact with the text inside.
        if (InfoPopover.Child is DependencyObject popoverChild
            && VisualTreeHelpers.IsDescendantOf(src, popoverChild)) return;
        // Click anywhere else: close.
        InfoPopover.IsOpen = false;
    }

    /// <summary>
    /// R70: right-align the open info popover so its right edge
    /// matches the pill's right edge. Called from a Dispatcher
    /// callback at Loaded priority so the popover's child has been
    /// measured and ActualWidth is available. We set
    /// VerticalOffset to 6 for the breathing-room gap below the
    /// pill (the default WPF placement aligns the popover's top
    /// with the pill's bottom, so 6px of visual gap reads better).
    /// </summary>
    private void AlignPopoverToPillRight()
    {
        if (!InfoPopover.IsOpen) return;
        var popoverChild = InfoPopover.Child as FrameworkElement;
        if (popoverChild == null) return;

        double pillWidth = InfoPillContent.ActualWidth;
        double popoverWidth = popoverChild.ActualWidth;
        if (popoverWidth <= 0) return;

        // Right-align: shift the popover left so its right edge
        // aligns with the pill's right edge. HorizontalOffset is
        // added to the default position (popover's left edge at
        // pill's left edge), so the shift is the difference between
        // the two widths.
        InfoPopover.HorizontalOffset = pillWidth - popoverWidth;
        InfoPopover.VerticalOffset = 6;

        // Screen-bottom safety: if the popover would extend past
        // the bottom of the work area, flip it above the pill.
        // Compute the popover's current screen bottom from the
        // pill's screen position + VerticalOffset + ActualHeight.
        var popoverTopOnScreen = InfoPillContent.PointToScreen(
            new Point(0, InfoPillContent.ActualHeight + InfoPopover.VerticalOffset)).Y;
        var popoverBottomOnScreen = popoverTopOnScreen + popoverChild.ActualHeight;
        var workArea = SystemParameters.WorkArea;
        if (popoverBottomOnScreen > workArea.Bottom)
        {
            // Flip above the pill: the popover's top should sit
            // 6px above the pill's top edge.
            InfoPopover.VerticalOffset = -(popoverChild.ActualHeight + 6);
        }
    }

    private void InfoPopover_Closed(object? sender, EventArgs e)
    {
        // Reset the cursor to the default so the InfoPill doesn't
        // look "pressed" after the popover closes. The cursor
        // property is set in XAML, so this is purely a visual nicety.
    }

    // ---- P1: tree floating popup XAML handlers ----
    // The 8px hot zone + popup use plain WPF MouseEnter /
    // MouseLeave / Closed events. The actual logic (open /
    // close / hide timer) lives on the fullscreen
    // controller — these handlers just forward.
    private void TreeHotZone_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        => _fullscreen.TreeHotZone_MouseEnter();
    private void TreeHotZone_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        => _fullscreen.TreeHotZone_MouseLeave();
    private void TreeFloatingPanel_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        => _fullscreen.TreeFloatingPanel_MouseEnter();
    private void TreeFloatingPanel_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        => _fullscreen.TreeFloatingPanel_MouseLeave();
    private void TreeFloatingPopup_Closed(object? sender, EventArgs e)
        => _fullscreen.TreeFloatingPopup_Closed();
}