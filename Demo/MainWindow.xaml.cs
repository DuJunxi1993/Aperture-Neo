using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Controls.Primitives;
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
        ImageViewer.StatusChanged += msg =>
            Dispatcher.Invoke(() => SetStatus(msg, false));

        FolderTree.FolderSelected += OnFolderSelected;
        FolderTree.DrillModeChanged += UpdateReturnToRootVisibility;
        ThumbGrid.ItemClicked += OnThumbClicked;

        _statusHideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        _statusHideTimer.Tick += (s, e) =>
        {
            if (!string.IsNullOrEmpty(StatusText.Text))
            {
                FadeOutStatus();
            }
            _statusHideTimer?.Stop();
        };

        Loaded += (_, _) =>
        {
            Focus();
            Dispatcher.BeginInvoke(new Action(() =>
            {
                PositionOverlayPopups();
                UpdateOverlayPopupsVisibility();
                var recent = App.SettingsStore.Recent;
                if (recent.Count > 0 && Directory.Exists(recent[0].Path))
                {
                    _navigation.LoadFolder(recent[0].Path);
                }
            }), DispatcherPriority.Loaded);
        };

        // Re-position floating popups whenever window/viewer size changes
        SizeChanged += (_, _) => PositionOverlayPopups();
        ImageViewer.SizeChanged += (_, _) => PositionOverlayPopups();
        ImageViewer.LayoutUpdated += (_, _) => PositionOverlayPopups();

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
        // Also show floating bar on mouse move in fullscreen
        if (!FloatingBarPopup.IsOpen)
        {
            FloatingBarPopup.IsOpen = true;
        }
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
            case Key.Left: if (col > 0) targetIdx = currentIdx - 1; else if (row > 0) targetIdx = currentIdx - cols; break;
            case Key.Right: if (col < cols - 1 && currentIdx + 1 < total) targetIdx = currentIdx + 1; else if (row < lastRow) targetIdx = currentIdx + cols; break;
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
        _thumbCoordinator.LoadForFolder(_navigation.Items, _navigation.CurrentIndex,
            msg => SetStatus(msg, true));
    }

    private void OnCurrentImageChanged(ImageItem item)
    {
        if (item == null) return;
        Title = $"Aperture Neo · {item.FileName} ({_navigation.CurrentIndex + 1}/{_navigation.Count})";
        ImageViewer.LoadImage(item.FilePath);
        ImageInfo.Text = $"{item.Width}×{item.Height}  ·  {FormatFileSize(item.FileSize)}";
        ImageIndexInfo.Text = $"{_navigation.CurrentIndex + 1}/{_navigation.Count}";
        ThumbGrid.SelectedItem = item;
        ThumbGrid.ScrollSelectedIntoView();
        if (ImageViewer.ContextMenu != null) ImageViewer.ContextMenu.IsOpen = false;
        PositionOverlayPopups();
    }

    /// <summary>
    /// Position the floating Popup overlays (FloatingBar, StatusPill, InfoPill)
    /// to align with the ImageViewer's screen coordinates.
    /// Popups are used because the SkiaImageViewer's WriteableBitmap render
    /// occludes sibling Borders in the same Grid cell.
    /// </summary>
    private void PositionOverlayPopups()
    {
        try
        {
            if (ImageViewer.ActualWidth <= 0 || ImageViewer.ActualHeight <= 0) return;
            var topLeft = ImageViewer.PointToScreen(new Point(0, 0));
            var w = ImageViewer.ActualWidth;
            var h = ImageViewer.ActualHeight;

            // FloatingBar: bottom-center
            const float FB_W = 320, FB_H = 44, FB_MARGIN = 18;
            FloatingBarPopup.HorizontalOffset = topLeft.X + (w - FB_W) / 2;
            FloatingBarPopup.VerticalOffset = topLeft.Y + h - FB_H - FB_MARGIN;

            // InfoPill: top-right
            const float IP_MARGIN = 14;
            // Measure to position right-aligned
            InfoPillPopup.HorizontalOffset = topLeft.X + w - InfoPill.ActualWidth - IP_MARGIN;
            InfoPillPopup.VerticalOffset = topLeft.Y + IP_MARGIN;

            // StatusPill: top-left
            StatusPillPopup.HorizontalOffset = topLeft.X + IP_MARGIN;
            StatusPillPopup.VerticalOffset = topLeft.Y + IP_MARGIN;
        }
        catch { }
    }

    private void UpdateOverlayPopupsVisibility()
    {
        FloatingBarPopup.IsOpen = !_isFullscreen;
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes >= 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
        if (bytes >= 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        if (bytes >= 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes} B";
    }

    private void SetStatus(string text, bool isError)
    {
        StatusText.Text = text;
        StatusText.Foreground = isError
            ? (Brush)FindResource("StatusRed")
            : (Brush)FindResource("TextTertiary");
        StatusPillPopup.IsOpen = true;
        StatusPill.Opacity = 1;
        PositionOverlayPopups();
        if (!isError) ResetToolbarHideTimer();
    }

    private void FadeOutStatus()
    {
        var anim = new DoubleAnimation
        {
            To = 0,
            Duration = TimeSpan.FromSeconds(0.4),
            FillBehavior = FillBehavior.Stop
        };
        anim.Completed += (_, _) =>
        {
            StatusPillPopup.IsOpen = false;
            StatusPill.Opacity = 1;
        };
        StatusPill.BeginAnimation(UIElement.OpacityProperty, anim);
    }

    private void BtnClearRecent_Click(object sender, RoutedEventArgs e)
    {
        App.SettingsStore.ClearRecent();
    }

    private async void BtnClearCache_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SetStatus("正在清除缓存…", false);
            await App.ThumbnailCache.ClearAsync();
            foreach (var item in _navigation.Items) { item.Thumbnail = null; item.HasThumbnailError = false; item.ThumbnailErrorMessage = null; }
            _thumbCoordinator.LoadForFolder(_navigation.Items, _navigation.CurrentIndex,
                msg => SetStatus(msg, true));
            SetStatus("缓存已清除", false);
        }
        catch (Exception ex) { DebugLog.Write("Cache", "clear fail", ex); SetStatus($"清除失败: {ex.Message}", true); }
    }

    private void BtnMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button b && b.ContextMenu != null) { b.ContextMenu.PlacementTarget = b; b.ContextMenu.Placement = PlacementMode.Bottom; b.ContextMenu.IsOpen = true; }
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
            FloatingBarPopup.IsOpen = false;
            InfoPillPopup.IsOpen = false;
            StatusPillPopup.IsOpen = false;
            HideToolbar();
        }
        else
        {
            WindowStyle = WindowStyle.SingleBorderWindow;
            WindowState = _prevWindowState;
            ApplyColumnVisibility();
            FloatingBarPopup.IsOpen = true;
            InfoPillPopup.IsOpen = true;
            ShowToolbar();
            _statusHideTimer?.Stop();
        }
        Dispatcher.BeginInvoke(() =>
        {
            UpdateLayout();
            ImageViewer.FitToScreen();
            Focus();
            PositionOverlayPopups();
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