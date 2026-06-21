using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using ApertureNeo.Controls.FolderTree;
using ApertureNeo.Helpers;
using ApertureNeo.Services;

namespace ApertureNeo;

/// <summary>
/// Window-chrome concerns: layout columns, overlay visibility, all
/// the simple click handlers (open/prev/next/fit/slideshow/full-
/// screen/tree/thumb), the keyboard nav dispatch (HandleKey),
/// drag-and-drop and the Open / About / Clear menu actions. The
/// largest, most stable controller in the partial-class split —
/// a good pilot for verifying the pattern before extracting the
/// more stateful controllers (fullscreen / edge-nav / tree /
/// info-popover).
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

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) { ToggleMaximize(); }
        else
        {
            if (WindowState == WindowState.Maximized)
            {
                var point = e.GetPosition(this);
                var screenPoint = PointToScreen(point);
                ResizeMode = ResizeMode.NoResize;
                WindowState = WindowState.Normal;
                MaximizeIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.Square24;
                Left = screenPoint.X - point.X;
                Top = screenPoint.Y - point.Y;
                Width = RestoreBounds.Width;
                Height = RestoreBounds.Height;
                ResizeMode = ResizeMode.CanResize;
            }
            if (WindowState == WindowState.Normal) DragMove();
        }
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
                case Key.O: BtnOpen_Click(this, new RoutedEventArgs()); return true;
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
                else if (_slideshow.IsRunning) { _slideshow.Stop(); UpdateSlideshowButton(); }
                return true;
            case Key.F5: ToggleSlideshow(); return true;
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

    private void OnFolderSelected(FolderSource source, string path)
    {
        _navigation.LoadFolder(path);
        // Only directories that contain at least one image are recorded
        // as recent visits. We can't use _navigation.Count here because
        // LoadFolder is asynchronous (it enumerates files on a worker
        // thread); by the time this line runs, the items haven't been
        // added yet. FormatHelper.FolderHasImages does a single-pass
        // check using the same supported-extension filter, so it's
        // consistent with what will end up in _items — and it's what
        // BtnOpen_Click / OnWindowDrop have always done.
        if (source != FolderSource.Favorite && source != FolderSource.Recent
            && FormatHelper.FolderHasImages(path))
        {
            App.SettingsStore.AddRecent(path);
        }
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

    private void BtnClearRecent_Click(object sender, RoutedEventArgs e)
    {
        App.SettingsStore.ClearRecent();
    }

    private async void BtnClearCache_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await App.ThumbnailCache.ClearAsync();
            foreach (var item in _navigation.Items) { item.Thumbnail = null; item.HasThumbnailError = false; item.ThumbnailErrorMessage = null; }
            _thumbCoordinator.LoadForFolder(_navigation.Items, _navigation.CurrentIndex, null);
        }
        catch (Exception ex) { DebugLog.Write("Cache", "clear fail", ex); }
    }

    private void BtnMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button b && b.ContextMenu != null)
        {
            // Anchor the popup menu to the button itself, not the mouse cursor.
            // Placement=Bottom places it directly below the button; PlacementTarget
            // is the button so the position stays correct even if the user
            // moved the mouse before clicking.
            b.ContextMenu.PlacementTarget = b;
            b.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            b.ContextMenu.IsOpen = true;
        }
    }

    private void UpdateThemeMenuChecks() { /* no-op: theme is global, the menu items don't need checkmarks */ }

    /// <summary>
    /// About dialog. The app is Linear light-mode only; the previous
    /// theme switcher menu and ApplyTheme / ThemeDark / ThemeLight /
    /// ThemeSystem handlers were removed in the "light-only" cleanup.
    ///
    /// The About window also drives the in-app update flow: it
    /// queries GitHub Releases on open and raises UpdateAvailableChanged
    /// whenever the state flips. We subscribe to that event so the
    /// "（有版本更新）" suffix appears next to this menu item the
    /// moment the user opens About and a new version is detected.
    /// </summary>
    private void About_Click(object sender, RoutedEventArgs e)
    {
        var win = new AboutWindow(this);
        win.UpdateAvailableChanged += OnAboutUpdateAvailableChanged;
        win.ShowDialog();
    }

    /// <summary>
    /// Toggle the "（有版本更新）" suffix on the 关于 menu item. The
    /// About window raises this event after every check (idle load
    /// + 重新检查 click) and again on close with the last-known
    /// state so the badge doesn't outlive the dialog.
    /// </summary>
    private void OnAboutUpdateAvailableChanged(object? sender, bool available)
    {
        if (MenuAbout == null) return;
        var suffix = FindAboutUpdateSuffix();
        if (suffix == null) return;
        if (available)
        {
            suffix.Text = "（有版本更新）";
            suffix.Visibility = Visibility.Visible;
        }
        else
        {
            suffix.Text = "";
            suffix.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Walk the MenuItem's Header (a StackPanel) to find the
    /// suffix TextBlock by name. Cached on first hit so we don't
    /// re-walk the visual tree on every event.
    /// </summary>
    private System.Windows.Controls.TextBlock? _aboutUpdateSuffixCache;
    private System.Windows.Controls.TextBlock? FindAboutUpdateSuffix()
    {
        if (_aboutUpdateSuffixCache != null) return _aboutUpdateSuffixCache;
        if (MenuAbout?.Header is not System.Windows.Controls.StackPanel stack) return null;
        foreach (var child in stack.Children)
        {
            if (child is System.Windows.Controls.TextBlock tb && tb.Name == "AboutUpdateSuffix")
            {
                _aboutUpdateSuffixCache = tb;
                return tb;
            }
        }
        return null;
    }

    private void BtnOpen_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = FormatHelper.Filter, Multiselect = false, RestoreDirectory = true };
        if (dialog.ShowDialog() != true) return;
        var folder = Path.GetDirectoryName(dialog.FileName);
        if (string.IsNullOrEmpty(folder)) return;
        if (FormatHelper.FolderHasImages(folder))
            App.SettingsStore.AddRecent(folder);
        _navigation.LoadFolder(folder);
        _navigation.NavigateTo(dialog.FileName);
    }

    private void BtnMinimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void BtnMaximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();
    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

    private void ToggleMaximize()
    {
        if (WindowState == WindowState.Maximized) { WindowState = WindowState.Normal; MaximizeIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.Square24; }
        else { WindowState = WindowState.Maximized; MaximizeIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.SquareMultiple24; }
    }

    private void BtnPrev_Click(object sender, RoutedEventArgs e) => _navigation.MovePrevious();
    private void BtnNext_Click(object sender, RoutedEventArgs e) => _navigation.MoveNext();
    private void BtnFit_Click(object sender, RoutedEventArgs e) => ImageViewer.FitToScreen();
    private void BtnSlideshow_Click(object sender, RoutedEventArgs e) => ToggleSlideshow();
    private void BtnFullscreen_Click(object sender, RoutedEventArgs e) => ToggleFullscreen();
    private void BtnToggleTree_Click(object sender, RoutedEventArgs e) => ToggleTreeColumn();
    private void BtnToggleThumb_Click(object sender, RoutedEventArgs e) => ToggleThumbColumn();

    private void ToggleTreeColumn()
    {
        if (_isFullscreen) return;
        _isTreeVisible = !_isTreeVisible;
        ApplyColumnVisibility();
    }

    private void ToggleThumbColumn()
    {
        if (_isFullscreen) return;
        _isThumbVisible = !_isThumbVisible;
        ApplyColumnVisibility();
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

    private void ToggleSlideshow() { _slideshow.Toggle(); UpdateSlideshowButton(); }

    private void UpdateSlideshowButton()
    {
        if (_slideshow.IsRunning) { SlideshowIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.Pause24; }
        else { SlideshowIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.Play20; }
    }
}
