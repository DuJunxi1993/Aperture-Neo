using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ApertureNeo.Controls;
using ApertureNeo.Controls.FolderTree;
using ApertureNeo.Helpers;
using ApertureNeo.Models;
using ApertureNeo.Services;
using Wpf.Ui.Controls;

namespace ApertureNeo;

public partial class MainWindow : FluentWindow
{
    private readonly NavigationService _navigation = new();
    private readonly SlideshowService _slideshow = new();
    private readonly ThumbnailLoadCoordinator _thumbCoordinator = new();

    private bool _isFullscreen;
    private bool _isTreeVisible = true;
    private bool _isThumbVisible = true;
    private WindowState _prevWindowState;
    private DispatcherTimer? _statusHideTimer;
    private Point _lastMousePosition;

    public MainWindow()
    {
        InitializeComponent();

        _navigation.CollectionChanged += OnCollectionChanged;
        _navigation.CurrentImageChanged += OnCurrentImageChanged;
        _slideshow.NextRequested += () => Dispatcher.Invoke(() => _navigation.MoveNext());

        ImageViewer.ZoomChanged += zoom =>
            Dispatcher.Invoke(() => ZoomTextBlock.Text = $"{zoom * 100:F0}%");
        // StatusChanged events are no longer surfaced — the previous StatusPill
        // UI was removed; LoadImage progress messages no longer reach the user.

        FolderTree.FolderSelected += OnFolderSelected;
        FolderTree.DrillModeChanged += UpdateReturnToRootVisibility;
        ThumbGrid.ItemClicked += OnThumbClicked;

        _statusHideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        _statusHideTimer.Tick += (s, e) =>
        {
            // Hide toolbar in fullscreen (it auto-showed when mouse moved near top)
            if (_isFullscreen && TitleBarArea.Visibility == Visibility.Visible)
                HideToolbar();
            _statusHideTimer?.Stop();
        };

        Loaded += (_, _) =>
        {
            Focus();
            Dispatcher.BeginInvoke(new Action(() =>
            {
                UpdateOverlayVisibility();

                var recent = App.SettingsStore.Recent;
                if (recent.Count > 0 && Directory.Exists(recent[0].Path))
                {
                    _navigation.LoadFolder(recent[0].Path);
                }
            }), DispatcherPriority.Loaded);
        };

        // Adorners auto-follow the window via the visual tree — no event handlers needed.

        Closed += (_, _) =>
        {
            _thumbCoordinator.Dispose();
            _slideshow.Dispose();
            _navigation.Dispose();
            _statusHideTimer?.Stop();
        };

        PreviewKeyDown += (_, e) => { if (HandleKey(e.Key)) e.Handled = true; };
        MouseMove += OnWindowMouseMove;
        Drop += OnWindowDrop;

        UpdateThemeMenuChecks();
    }

    public MainWindow(string filePath) : this()
    {
        if (File.Exists(filePath))
        {
            var folder = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
            {
                if (FormatHelper.FolderHasImages(folder))
                    App.SettingsStore.AddRecent(folder);
                _navigation.LoadFolder(folder);
                _navigation.NavigateTo(filePath);
            }
        }
    }

    private void OnWindowMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isFullscreen) return;
        var pos = e.GetPosition(this);
        if (Math.Abs(pos.X - _lastMousePosition.X) > 5 || Math.Abs(pos.Y - _lastMousePosition.Y) > 5)
        {
            _lastMousePosition = pos;
            if (TitleBarArea.Visibility != Visibility.Visible) ShowToolbar();
            ResetToolbarHideTimer();
        }
        // Floating bar is now an Adorner (always attached) — no IsOpen toggle needed.
    }

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

    private void BtnReturnToRoot_Click(object sender, RoutedEventArgs e)
    {
        FolderTree.ReturnToRoot();
    }

    private void UpdateReturnToRootVisibility()
    {
        BtnReturnToRoot.Visibility = FolderTree.IsInDrillMode ? Visibility.Visible : Visibility.Collapsed;
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

    private void ShowToolbar()
    {
        TitleBarArea.Visibility = Visibility.Visible;
        TitleBarRow.Height = new GridLength(44);
    }

    private void HideToolbar()
    {
        TitleBarArea.Visibility = Visibility.Collapsed;
        TitleBarRow.Height = new GridLength(0);
    }

    private void ResetToolbarHideTimer()
    {
        _statusHideTimer?.Stop();
        _statusHideTimer?.Start();
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
        if (source != FolderSource.Favorite && source != FolderSource.Recent
            && _navigation.Count > 0)
        {
            App.SettingsStore.AddRecent(path);
        }
    }

    private void OnThumbClicked(ImageItem item) { _navigation.NavigateTo(item.FilePath); }

    private void OnCollectionChanged()
    {
        ThumbGrid.ItemsSource = _navigation.Items;
        ThumbEmpty.Visibility = _navigation.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _thumbCoordinator.LoadForFolder(_navigation.Items, _navigation.CurrentIndex, null);
    }

    /// <summary>
    /// Triggered by the thumbnail ScrollViewer. Estimates which item
    /// indices are visible based on scroll offset and the configured
    /// thumbnail size, then asks the coordinator to load any unloaded
    /// thumbnails in that range. Cheap O(1) work per scroll tick.
    /// </summary>
    private void ThumbScroller_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_navigation.Count == 0) return;
        // Approximate visible range. Each row is ~152px tall (140 thumb + margins).
        const double rowHeight = 152.0;
        int firstRow = Math.Max(0, (int)(e.VerticalOffset / rowHeight));
        int visibleRows = Math.Max(1, (int)(e.ViewportHeight / rowHeight) + 1);
        // Each row has ~2 columns in a typical 360px panel — but we don't know
        // exact column count; use a generous range so the lazy loader covers
        // the visible band even with imprecise math.
        int firstIdx = Math.Max(0, firstRow * 2 - 2);
        int lastIdx = Math.Min(_navigation.Count - 1, (firstRow + visibleRows) * 2 + 1);
        _thumbCoordinator.EnsureVisible(firstIdx, lastIdx);
    }

    private void OnCurrentImageChanged(ImageItem item)
    {
        if (item == null) return;
        Title = $"Aperture Neo · {item.FileName} ({_navigation.CurrentIndex + 1}/{_navigation.Count})";
        ImageViewer.LoadImage(item.FilePath);
        ImageInfo.Text = $"{item.Width} × {item.Height}    ·    {FormatFileSize(item.FileSize)}";
        ImageIndexInfo.Text = $"{_navigation.CurrentIndex + 1}/{_navigation.Count}";
        ThumbGrid.SelectedItem = item;
        ThumbGrid.ScrollSelectedIntoView();
        if (ImageViewer.ContextMenu != null) ImageViewer.ContextMenu.IsOpen = false;
    }

    /// <summary>
    /// Show/hide the floating overlays based on fullscreen state.
    /// All overlays stay visible in fullscreen — they're useful precisely when
    /// the user is in fullscreen (no other UI is shown).
    /// </summary>
    private void UpdateOverlayVisibility()
    {
        FloatingBarContent.Visibility = Visibility.Visible;
        InfoPillContent.Visibility = Visibility.Visible;
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes >= 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
        if (bytes >= 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        if (bytes >= 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes} B";
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

    private void UpdateThemeMenuChecks() { /* locked dark */ }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        new AboutWindow(this).ShowDialog();
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
        TreeColumn.Width = _isTreeVisible ? new GridLength(232) : new GridLength(0);
        TreeColumn.MinWidth = _isTreeVisible ? 180 : 0;
        TreeSplitter.Visibility = _isTreeVisible ? Visibility.Visible : Visibility.Collapsed;
        ThumbColumn.Width = _isThumbVisible ? new GridLength(360) : new GridLength(0);
        ThumbColumn.MinWidth = _isThumbVisible ? 240 : 0;
        ThumbSplitter.Visibility = _isThumbVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ToggleSlideshow() { _slideshow.Toggle(); UpdateSlideshowButton(); }

    private void UpdateSlideshowButton()
    {
        if (_slideshow.IsRunning) { SlideshowIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.Pause24; }
        else { SlideshowIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.Play20; }
    }

    private void ToggleFullscreen()
    {
        _isFullscreen = !_isFullscreen;
        if (_isFullscreen)
        {
            _prevWindowState = WindowState;
            WindowStyle = WindowStyle.None;
            WindowState = WindowState.Maximized;
            TreeColumn.Width = new GridLength(0); TreeColumn.MinWidth = 0; TreeSplitter.Visibility = Visibility.Collapsed;
            ThumbColumn.Width = new GridLength(0); ThumbColumn.MinWidth = 0; ThumbSplitter.Visibility = Visibility.Collapsed;
            UpdateOverlayVisibility();
            HideToolbar();
        }
        else
        {
            WindowStyle = WindowStyle.SingleBorderWindow;
            WindowState = _prevWindowState;
            ApplyColumnVisibility();
            UpdateOverlayVisibility();
            ShowToolbar();
            _statusHideTimer?.Stop();
        }
        Dispatcher.BeginInvoke(() =>
        {
            UpdateLayout();
            ImageViewer.FitToScreen();
            Focus();
        }, DispatcherPriority.Loaded);
    }

    private void ZoomTextBlock_Click(object sender, MouseButtonEventArgs e) => ImageViewer.ZoomToOriginal();

    private void CtxCopyPath_Click(object sender, RoutedEventArgs e)
    { if (_navigation.Current == null) return; try { Clipboard.SetText(_navigation.Current.FilePath); } catch { } }

    private void CtxOpenInExplorer_Click(object sender, RoutedEventArgs e)
    { if (_navigation.Current == null) return; try { System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{_navigation.Current.FilePath}\""); } catch { } }

    private void CtxPrint_Click(object sender, RoutedEventArgs e)
    {
        if (_navigation.Current == null) return;
        var dialog = new PrintDialog();
        if (dialog.ShowDialog() == true) dialog.PrintVisual(ImageViewer, _navigation.Current.FileName);
    }

    private void CtxSetWallpaper_Click(object sender, RoutedEventArgs e)
    { if (_navigation.Current != null) WallpaperService.TrySetDesktop(_navigation.Current.FilePath); }
}