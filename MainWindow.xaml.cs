using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
    // 8 parallel decodes: roughly matches modern CPU core count; the
    // old default of 2 wasted most of the available IO+decode bandwidth
    // and made folder loads feel sluggish past ~50 items.
    private readonly ThumbnailLoadCoordinator _thumbCoordinator = new(maxConcurrent: 8);

    private bool _isFullscreen;
    private bool _isTreeVisible = true;
    private bool _isThumbVisible = true;
    private WindowState _prevWindowState;
    private DispatcherTimer? _overlayHideTimer;
    private Point _lastMousePosition;
    /// <summary>
    /// True while edge-nav buttons are mid-fade or fully shown. Suppresses
    /// re-triggering the show animation on every micro mouse-move event
    /// (otherwise the Opacity would fight the timer restart constantly).
    /// </summary>
    private bool _edgeNavVisible;

    public MainWindow()
    {
        InitializeComponent();

        // R70: window-level click-outside handler that dismisses
        // the info popover when the user clicks anywhere outside
        // both the pill and the popover body. Hooked here in the
        // constructor so the subscription is active before any
        // click event can fire.
        PreviewMouseLeftButtonDown += OnWindowPreviewMouseLeftButtonDown;

        // Round 67: clamp file/thumb column widths based on the
        // current window size. TreeColumn cap is 1/3 of the window
        // width; ThumbColumn cap keeps the viewer at >= 400px so
        // the viewer never becomes invisible. SizeChanged fires on
        // initial layout AND every resize/maximize/restore, so
        // this single subscription covers all cases.
        SizeChanged += (_, e) =>
        {
            if (e.NewSize.Width > 0)
            {
                TreeColumn.MaxWidth = e.NewSize.Width / 3.0;
                ThumbColumn.MaxWidth = Math.Max(160, e.NewSize.Width - 400);
            }
        };

        _navigation.CollectionChanged += OnCollectionChanged;
        _navigation.CurrentImageChanged += OnCurrentImageChanged;
        _slideshow.NextRequested += () => Dispatcher.Invoke(() => _navigation.MoveNext());

        ImageViewer.ZoomChanged += zoom =>
            Dispatcher.Invoke(() => ZoomTextBlock.Text = $"{zoom * 100:F0}%");
        // StatusChanged events are no longer surfaced — the previous StatusPill
        // UI was removed; LoadImage progress messages no longer reach the user.
        // ImageLoaded fires after a successful load; copy authoritative
        // dimensions onto the corresponding ImageItem so the info pill
        // (and any other bound consumers) reflect the right values
        // without us touching the file twice.
        ImageViewer.ImageLoaded += result =>
        {
            var item = _navigation.Items.FirstOrDefault(i => i.FilePath == result.FilePath);
            if (item == null) return;
            item.SetDimensions(result.Width, result.Height);
            // If this is the currently-displayed image, refresh the info
            // pill right away — the lazy property probe and the authoritative
            // decode can both leave the pill empty until we reformat it.
            if (ReferenceEquals(item, _navigation.Current))
                UpdateCurrentImageInfo(item);
        };
        // When the lazy header-probe on the current item finishes, ImageItem
        // raises PropertyChanged on Width/Height — refresh the pill then so
        // the text updates from "?" to the real value without waiting for
        // the full decode.
        _navigation.CurrentImageChanged += item =>
        {
            if (item == null) return;
            item.PropertyChanged += (_, _) =>
            {
                if (ReferenceEquals(item, _navigation.Current))
                    Dispatcher.Invoke(() => UpdateCurrentImageInfo(item));
            };
        };

        FolderTree.FolderSelected += OnFolderSelected;
        FolderTree.DrillModeChanged += UpdateReturnToRootVisibility;
        ThumbGrid.ItemClicked += OnThumbClicked;

        _overlayHideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.0) };
        _overlayHideTimer.Tick += (s, e) =>
        {
            // Fullscreen only: fade the edge-nav buttons out after 3s of
            // mouse inactivity. The TitleBar/FloatingBar/InfoPill are NOT
            // re-shown by the timer — they stay collapsed the whole time
            // the window is in fullscreen, and are restored synchronously
            // on exit (see ExitFullscreenMode).
            if (_isFullscreen) HideEdgeNav();
            _overlayHideTimer?.Stop();
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
            // Persist the currently displayed image so a theme switch
            // (which closes+reopens the window) can restore position.
            // We write directly to the static SettingsStore so it
            // doesn't race with App.OnExit's save.
            var current = _navigation.Current;
            if (current != null)
                App.SettingsStore.LastOpenedImage = current.FilePath;

            _thumbCoordinator.Dispose();
            _slideshow.Dispose();
            _navigation.Dispose();
            _overlayHideTimer?.Stop();
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
            // Fullscreen: ONLY show the edge-nav buttons. The TitleBar,
            // FloatingBar, and InfoPill stay collapsed for the entire
            // fullscreen session and are restored synchronously on exit
            // — they must never appear in response to mouse activity.
            ShowEdgeNav();
            ResetOverlayHideTimer();
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

    /// <summary>
    /// Show the fullscreen edge-nav buttons (left + right). Cancels any
    /// in-flight fade-out, makes the borders Visible, and animates
    /// Opacity 0→1 over 200ms. Subsequent calls while already visible
    /// are a no-op — the timer restart is the only side-effect of
    /// repeated mouse-move events.
    /// </summary>
    private void ShowEdgeNav()
    {
        if (_edgeNavVisible) { ResetOverlayHideTimer(); return; }
        _edgeNavVisible = true;
        EdgeNavLeftContent.BeginAnimation(UIElement.OpacityProperty, null);
        EdgeNavRightContent.BeginAnimation(UIElement.OpacityProperty, null);
        EdgeNavLeftContent.Visibility = Visibility.Visible;
        EdgeNavRightContent.Visibility = Visibility.Visible;
        var fadeIn = new DoubleAnimation(0d, 1d, TimeSpan.FromMilliseconds(200));
        EdgeNavLeftContent.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        EdgeNavRightContent.BeginAnimation(UIElement.OpacityProperty, fadeIn);
    }

    /// <summary>
    /// Fade the edge-nav buttons out (200ms) and collapse them once the
    /// animation completes. Safe to call when already hidden.
    /// </summary>
    private void HideEdgeNav()
    {
        if (!_edgeNavVisible) return;
        _edgeNavVisible = false;
        var fadeOut = new DoubleAnimation(1d, 0d, TimeSpan.FromMilliseconds(200));
        fadeOut.Completed += (_, _) =>
        {
            if (_edgeNavVisible) return; // re-shown mid-fade; leave it
            EdgeNavLeftContent.Visibility = Visibility.Collapsed;
            EdgeNavRightContent.Visibility = Visibility.Collapsed;
            EdgeNavLeftContent.Opacity = 0;
            EdgeNavRightContent.Opacity = 0;
        };
        EdgeNavLeftContent.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        EdgeNavRightContent.BeginAnimation(UIElement.OpacityProperty, fadeOut);
    }

    private void ResetOverlayHideTimer()
    {
        _overlayHideTimer?.Stop();
        _overlayHideTimer?.Start();
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

    /// <summary>
    /// The ListBox (ThumbGrid) drives scrolling through its own
    /// internal ScrollViewer, which does pixel scrolling (Round
    /// 54 set <c>CanContentScroll=false</c> on ThumbGrid so the
    /// ScrollViewer scrolls by pixels, not items, which is what
    /// we want for a thumbnail grid). The inner ScrollViewer is
    /// created lazily from the ListBox template and isn't
    /// addressable directly from XAML. Hook Loaded on the
    /// ListBox (fires after the template has been applied and
    /// the visual tree is fully built), walk down to find the
    /// first ScrollViewer descendant, and subscribe
    /// ScrollChanged.
    /// </summary>
    private void ThumbGrid_Loaded(object sender, RoutedEventArgs e)
    {
        var innerScroller = FindVisualChild<ScrollViewer>(ThumbGrid);
        if (innerScroller == null) return;
        innerScroller.ScrollChanged += ThumbScroller_ScrollChanged;
        // Round 68: wire the ThumbnailCache size provider to the
        // AutoFitPanel that lives inside ThumbGrid.ItemsPanel.
        // The cache was constructed in App.OnStartup with a default
        // 256px size; now that the panel exists, we can hand the
        // cache a live source that returns the actual per-cell
        // width each time a thumbnail is generated. A second
        // resolution pass on column resize re-evaluates the size
        // because the Func is captured by reference, not value.
        var autoFit = FindVisualChild<AutoFitPanel>(ThumbGrid);
        if (autoFit != null)
        {
            App.ThumbnailCache.SetSizeProvider(() => (int)autoFit.ActualItemWidth);
        }
        // Re-raise the Loaded signal so handlers that depend on
        // the inner ScrollViewer's existence can re-run. Currently
        // no such dependency, but the call is cheap and future-
        // proofs against silent load-order issues.
        ThumbGrid_Ready?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Fired once ThumbGrid's template has been applied and the
    /// inner ScrollViewer is reachable. Currently unused; kept
    /// as a hook for any future initialisation that needs the
    /// inner ScrollViewer (e.g. custom keyboard navigation).
    /// </summary>
    public event EventHandler? ThumbGrid_Ready;

    /// <summary>
    /// Walk the visual tree depth-first and return the first
    /// descendant of type <typeparamref name="T"/>. Used to
    /// locate the ListBox's internal ScrollViewer from code
    /// without keeping a direct XAML reference to it (its x:Name
    /// is on a ListBox template part, not directly accessible).
    /// </summary>
    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T t) return t;
            var found = FindVisualChild<T>(child);
            if (found != null) return found;
        }
        return null;
    }

    private void OnCurrentImageChanged(ImageItem item)
    {
        if (item == null) return;
        Title = $"Aperture Neo · {item.FileName} ({_navigation.CurrentIndex + 1}/{_navigation.Count})";
        ImageViewer.LoadImage(item.FilePath);
        UpdateCurrentImageInfo(item);
        ImageIndexInfo.Text = $"{_navigation.CurrentIndex + 1}/{_navigation.Count}";
        ThumbGrid.SelectedItem = item;
        ThumbGrid.ScrollSelectedIntoView();
        if (ImageViewer.ContextMenu != null) ImageViewer.ContextMenu.IsOpen = false;
    }

    /// <summary>
    /// Update the info pill text. The image item may not yet have its
    /// dimensions populated — they're probed asynchronously from the file
    /// header on first access and then overwritten by the authoritative
    /// dimensions once SkiaImageViewer has decoded the image. Because
    /// ImageItem raises PropertyChanged when those values arrive, we
    /// re-invoke this method so the pill text stays in sync.
    /// </summary>
    private void UpdateCurrentImageInfo(ImageItem item)
    {
        if (item == null) return;
        // Show only resolution; the file size used to be appended here but
        // the user found it redundant with the floating bar's 1/82 counter
        // and the title-bar filename. Empty string hides the pill gracefully
        // when dimensions aren't available yet.
        ImageInfo.Text = (item.Width, item.Height) switch
        {
            (int w, int h) => $"{w} × {h}",
            _ => string.Empty
        };
        // The status dot to the left of the resolution text is gray
        // while dimensions are still unknown and turns brand-indigo the
        // moment SkiaImageViewer has decoded the image. This is a
        // cheap "loading" affordance that doesn't need its own pill.
        if (item.Width.HasValue && item.Height.HasValue)
            InfoPillDot.Background = (System.Windows.Media.Brush)FindResource("BrandPrimary");
        else
            InfoPillDot.Background = (System.Windows.Media.Brush)FindResource("TextQuat");
    }

    /// <summary>
    /// R70: toggle the info popover when the user clicks the
    /// InfoPill in the top-right of the viewer. Clicking the pill
    /// again closes the popover. Clicking anywhere outside both the
    /// pill and the popover also closes it (handled by the
    /// <see cref="OnWindowPreviewMouseLeftButtonDown"/> global
    /// handler). The popover is opened with
    /// <c>StaysOpen=true</c> because we manage its lifetime
    /// ourselves — the default <c>StaysOpen=false</c> would have
    /// the popover close itself on the very MouseDown that opened
    /// it (the pill click), which is the bug we hit in the last
    /// round.
    /// </summary>
    private void InfoPillContent_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (InfoPopover.IsOpen)
        {
            InfoPopover.IsOpen = false;
            return;
        }
        var item = _navigation.Current;
        if (item == null) return;
        PopulateInfoPopover(item);
        // We manage close-vs-stay-open ourselves (via the global
        // click-outside handler) so the popover's own auto-close
        // behavior would only interfere. StaysOpen=true disables
        // that auto-close.
        InfoPopover.StaysOpen = true;
        // Reset HorizontalOffset before opening so the popover
        // starts at the default left-aligned position; we'll
        // right-align it in a Dispatcher callback once the popover
        // has been measured and we know its actual width.
        InfoPopover.HorizontalOffset = 0;
        InfoPopover.IsOpen = true;
        // R70: right-align the popover's right edge with the pill's
        // right edge. We do this in a Dispatcher callback at
        // DispatcherPriority.Loaded so the popover's child has been
        // measured (ActualWidth is > 0) before we read it. The
        // callback also re-evaluates the screen-bottom clip and
        // flips the popover above the pill if needed.
        Dispatcher.BeginInvoke(new Action(AlignPopoverToPillRight),
            DispatcherPriority.Loaded);
    }

    /// <summary>
    /// R70: close the info popover when the user clicks anywhere
    /// outside both the InfoPill and the popover itself. We hook
    /// PreviewMouseLeftButtonDown at the window level (tunneling)
    /// so we see every click before the popup's own auto-close
    /// logic runs. Clicks on the pill itself are ignored here
    /// because the pill's own MouseLeftButtonUp handler toggles
    /// the popover; clicks on the popover's body are ignored
    /// because we want the user to be able to select text inside
    /// the popover (e.g. the file name) without dismissing it.
    /// </summary>
    private void OnWindowPreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!InfoPopover.IsOpen) return;
        var src = e.OriginalSource as DependencyObject;
        // Click on the pill: the pill's MouseLeftButtonUp toggles.
        if (IsDescendantOf(src, InfoPillContent)) return;
        // Click inside the popover's content: keep open so the
        // user can select / interact with the text inside.
        if (InfoPopover.Child is DependencyObject popoverChild
            && IsDescendantOf(src, popoverChild)) return;
        // Click anywhere else: close.
        InfoPopover.IsOpen = false;
    }

    private static bool IsDescendantOf(DependencyObject? node, DependencyObject? ancestor)
    {
        if (node == null || ancestor == null) return false;
        while (node != null)
        {
            if (node == ancestor) return true;
            node = System.Windows.Media.VisualTreeHelper.GetParent(node);
        }
        return false;
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

    /// <summary>
    /// R70: custom placement for the info popover. Default WPF
    /// placement ("Bottom") puts the popover's top-left under the
    /// pill's top-left, which can push the right edge past the
    /// window border on narrow viewports. We want the popover's
    /// right edge to align with the pill's right edge instead —
    /// computed as <c>x = pillWidth - popupWidth</c> with a 6px
    /// vertical offset. If the computed position would clip the
    /// screen's bottom edge, we flip the popover to display above
    /// the pill instead.
    /// </summary>
    /// <summary>
    /// R69: fill the popover's text blocks from the current
    /// ImageItem. The BitmapMetadata EXIF query is lazy (kicks off
    /// a background header read on first access), so Make/Model/Date
    /// may render as "—" on the first click and as the real values
    /// on subsequent clicks once the probe has landed. The popover
    /// re-queries on every open, so by the time the user clicks
    /// again, EXIF is typically populated.
    /// </summary>
    private void PopulateInfoPopover(ImageItem item)
    {
        // Section 1: file name (single line, ellipsized at the right)
        PopoverFileName.Text = string.IsNullOrEmpty(item.FileName) ? "—" : item.FileName;

        // Section 2: size + dimensions
        PopoverSize.Text = FormatFileSize(item.FileSize);
        if (item.Width.HasValue && item.Height.HasValue)
        {
            PopoverDimensions.Text = $"{item.Width} × {item.Height}";
        }
        else
        {
            PopoverDimensions.Text = "—";
        }

        // Section 3: EXIF (Make/Model/DateTaken)
        // BitmapMetadata only exists for JPEG. For other formats or
        // for files without an EXIF block, every query returns null
        // and the text blocks stay as their placeholder dash.
        var exif = item.Exif; // may be null (probe still in flight)
        if (exif != null)
        {
            // IFD0 tags: 271 Make, 272 Model, 306 DateTime (format "YYYY:MM:DD HH:MM:SS")
            var make = item.GetExifValue("/app1/ifd/{ushort=271}");
            var model = item.GetExifValue("/app1/ifd/{ushort=272}");
            var dateTaken = item.GetExifValue("/app1/ifd/{ushort=306}");
            PopoverExifMake.Text = string.IsNullOrEmpty(make) ? "—" : make;
            PopoverExifModel.Text = string.IsNullOrEmpty(model) ? "—" : model;
            // EXIF DateTime is the raw "YYYY:MM:DD HH:MM:SS" form.
            // We display it as-is — anything more elaborate would
            // require parsing the string, which doesn't add much.
            PopoverExifDate.Text = string.IsNullOrEmpty(dateTaken) ? "—" : dateTaken;
        }
        else
        {
            PopoverExifMake.Text = PopoverExifModel.Text = PopoverExifDate.Text = "—";
        }
    }

    private void InfoPopover_Closed(object? sender, EventArgs e)
    {
        // Reset the cursor to the default so the InfoPill doesn't
        // look "pressed" after the popover closes. The cursor
        // property is set in XAML, so this is purely a visual nicety.
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

    private void UpdateThemeMenuChecks() { /* no-op: theme is global, the menu items don't need checkmarks */ }

    /// <summary>
    /// About dialog. The app is Linear light-mode only; the previous
    /// theme switcher menu and ApplyTheme / ThemeDark / ThemeLight /
    /// ThemeSystem handlers were removed in the "light-only" cleanup.
    /// </summary>
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
        ThumbColumn.Width = _isThumbVisible ? new GridLength(400) : new GridLength(0);
        ThumbColumn.MinWidth = _isThumbVisible ? 160 : 0;
        // Round 48 removed the ThumbSplitter (the viewer column
        // now sits flush against the thumbnail column with no
        // seam), so the splitter's Visibility toggle is no longer
        // needed. The reference is kept commented out below for
        // a future round-trip in case a splitter is reintroduced.
        // The 8px left hot zone is only meaningful when the inline
        // tree is collapsed. In fullscreen we hide it entirely; in
        // non-fullscreen it's shown iff the tree column is collapsed.
        TreeHotZone.Visibility =
            _isFullscreen ? Visibility.Collapsed :
            _isTreeVisible ? Visibility.Collapsed :
            Visibility.Visible;
        // If the tree just re-opened, dismiss any floating popup.
        if (_isTreeVisible) TreeFloatingPopup.IsOpen = false;
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
            ThumbColumn.Width = new GridLength(0); ThumbColumn.MinWidth = 0;
            // Synchronously hide TitleBar, FloatingBar, InfoPill, AND the
            // edge nav buttons. No mouse-event trigger can re-show the
            // first three for the duration of the fullscreen session.
            UpdateOverlayVisibility();
            _edgeNavVisible = false;
            EdgeNavLeftContent.Visibility = Visibility.Collapsed;
            EdgeNavRightContent.Visibility = Visibility.Collapsed;
            EdgeNavLeftContent.Opacity = 0;
            EdgeNavRightContent.Opacity = 0;
            _overlayHideTimer?.Stop();
        }
        else
        {
            WindowStyle = WindowStyle.SingleBorderWindow;
            WindowState = _prevWindowState;
            ApplyColumnVisibility();
            // Synchronously restore all 3 chrome elements + force-hide
            // the edge nav (which is fullscreen-only).
            UpdateOverlayVisibility();
        }
        Dispatcher.BeginInvoke(() =>
        {
            UpdateLayout();
            ImageViewer.FitToScreen();
            Focus();
        }, DispatcherPriority.Loaded);
    }

    private void ZoomTextBlock_Click(object sender, MouseButtonEventArgs e) => ImageViewer.ZoomToOriginal();

    // ---- Floating file bar (hot zone + popup) ----

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
