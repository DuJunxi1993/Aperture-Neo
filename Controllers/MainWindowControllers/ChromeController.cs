using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using ApertureNeo.Helpers;
using ApertureNeo.ViewModels;

namespace ApertureNeo;

/// <summary>
/// Window-chrome concerns: drag-and-drop file open + keyboard
/// nav dispatch (HandleKey + LinearNavigate + TryGridNavigate).
/// Most of the chrome (column visibility, overlay visibility,
/// fullscreen toggles, slideshow timer, button click handlers)
/// is now driven by VMs and IUiState — this file is just the
/// leftover window-level input glue.
///
/// P2 cleanup: removed ~340 lines of dead code that was
/// replaced by VM commands in P1 + early P2:
///   - All BtnXxx_Click handlers (TitleBarView / FloatingBarView
///     bind to VM commands directly).
///   - About_Click + OnAboutUpdateAvailableChanged +
///     FindAboutUpdateSuffix (TitleBarView XAML DataTrigger
///     drives the "（有版本更新）" suffix via IsUpdateAvailable;
///     OpenAboutRequested event opens the AboutWindow).
///   - ToggleTreeColumn / ToggleThumbColumn (handled by
///     IUiState setters from TitleBarViewModel).
///   - ToggleSlideshow (replaced by FloatingBarViewModel
///     ToggleSlideshowCommand; the timer loop will move into
///     a future IUiState.IsSlideshowRunning observer in a
///     later P2 commit).
///   - OnFolderSelected (moved into FolderTreePanelVM).
///   - TitleBar_MouseLeftButtonDown (moved into TitleBarView).
///   - UpdateThemeMenuChecks (no-op).
/// </summary>
public partial class MainWindow
{
    private void OnWindowDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var files = (string[])e.Data.GetData(DataFormats.FileDrop);
        if (files == null || files.Length == 0) return;
        var file = files[0];
        if (!File.Exists(file) || !FormatHelper.IsSupported(file)) return;
        var folder = Path.GetDirectoryName(file);
        if (string.IsNullOrEmpty(folder)) return;
        if (FormatHelper.FolderHasImages(folder))
            App.SettingsStore.AddRecent(folder);
        _navigation.LoadFolder(folder);
        _navigation.NavigateTo(file);
    }

    private bool HandleKey(Key key)
    {
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) return false;

        if (ctrl)
        {
            switch (key)
            {
                case Key.F: ToggleFullscreen(); return true;
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
                if (_isFullscreen) ToggleFullscreen();
                else if (_slideshow.IsRunning) { _slideshow.Stop(); }
                return true;
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
        if (_isFullscreen) return false;
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

    /// <summary>
    /// Show/hide the persistent overlay chrome based on fullscreen state.
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
        if (_isFullscreen)
        {
            TitleBarArea.Visibility = Visibility.Collapsed;
            TitleBarRow.Height = new GridLength(0);
            FloatingBarContent.Visibility = Visibility.Collapsed;
            InfoPillContent.Visibility = Visibility.Collapsed;
            // Edge nav is left in whatever state it was — entering
            // fullscreen hides it synchronously via EnterFullscreenMode,
            // and subsequent mouse moves will reveal it.
        }
        else
        {
            TitleBarArea.Visibility = Visibility.Visible;
            TitleBarRow.Height = new GridLength(44);
            FloatingBarContent.Visibility = Visibility.Visible;
            InfoPillContent.Visibility = Visibility.Visible;
            // Leaving fullscreen: force the edge nav hidden and stop the
            // timer so it doesn't fire after we've restored normal chrome.
            _edgeNavVisible = false;
            EdgeNavLeftContent.Visibility = Visibility.Collapsed;
            EdgeNavRightContent.Visibility = Visibility.Collapsed;
            EdgeNavLeftContent.Opacity = 0;
            EdgeNavRightContent.Opacity = 0;
            _overlayHideTimer?.Stop();
        }
    }

    private void ApplyColumnVisibility()
    {
        // Fullscreen: collapse ALL side chrome (columns, splitters, hot
        // zones) so the viewer column truly owns the visible area. The
        // 6px ThumbSplitterColumn and the Auto TreeSplitterColumn would
        // otherwise stay reserved as transparent gaps that show the
        // window's SurfaceCanvas background through to the viewer edge,
        // producing visible white-ish seams on top of the white viewer.
        if (_isFullscreen)
        {
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

        TreeColumn.Width = _isTreeVisible ? new GridLength(232) : new GridLength(0);
        TreeColumn.MinWidth = _isTreeVisible ? 180 : 0;
        TreeSplitterColumn.Width = _isTreeVisible ? new GridLength(1, GridUnitType.Auto) : new GridLength(0);
        TreeSplitter.Visibility = _isTreeVisible ? Visibility.Visible : Visibility.Collapsed;
        ThumbColumn.Width = _isThumbVisible ? new GridLength(400) : new GridLength(0);
        ThumbColumn.MinWidth = _isThumbVisible ? 160 : 0;
        ThumbSplitterColumn.Width = _isThumbVisible ? new GridLength(6) : new GridLength(0);
        ThumbSplitter.Visibility = _isThumbVisible ? Visibility.Visible : Visibility.Collapsed;
        // The 8px left hot zone is only meaningful when the inline
        // tree is collapsed. Fullscreen is handled in the early-return
        // branch above.
        TreeHotZone.Visibility = _isTreeVisible ? Visibility.Collapsed : Visibility.Visible;
        // If the tree just re-opened, dismiss any floating popup.
        if (_isTreeVisible) TreeFloatingPopup.IsOpen = false;
    }
}