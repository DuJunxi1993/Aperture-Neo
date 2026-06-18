using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ApertureNeo.Controls;
using ApertureNeo.Controls.FolderTree;
using ApertureNeo.Helpers;
using ApertureNeo.Models;
using ApertureNeo.Services;
using Wpf.Ui.Controls;

namespace ApertureNeo;

/// <summary>
/// Main viewer window. Three-column layout
/// (FolderTree | ThumbnailGrid | SkiaImageViewer), fullscreen state
/// machine, slideshow + keyboard nav, and most app-level event
/// wiring. Long-lived state lives in <see cref="NavigationService"/>
/// and <see cref="SlideshowService"/>; this class orchestrates
/// them and proxies UI events.
/// </summary>
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
    private DispatcherTimer? _exitHintHideTimer;
    private Point _lastMousePosition;
    // Cached in ThumbGrid_Loaded. Used by ThumbScroller_ScrollChanged to
    // convert a scroll offset/viewport into the actual visible item
    // range (real row height + real column count from the panel's
    // measure pass, not hardcoded guesses).
    private AutoFitPanel? _autoFit;
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

        // Fullscreen: the exit-hint pill shows on entry and hides itself
        // after 3s (no longer tied to cursor position). Reset on every
        // fullscreen entry; cancelled on exit.
        _exitHintHideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.0) };
        _exitHintHideTimer.Tick += (s, e) =>
        {
            if (_isFullscreen) HideExitFullscreenHint();
            _exitHintHideTimer.Stop();
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

    private void CtxCopyPath_Click(object sender, RoutedEventArgs e)
    { if (_navigation.Current == null) return; try { Clipboard.SetText(_navigation.Current.FilePath); } catch { } }

    private void CtxOpenInExplorer_Click(object sender, RoutedEventArgs e)
    { if (_navigation.Current == null) return; ShellHelper.RevealInExplorer(_navigation.Current.FilePath); }

    private void CtxPrint_Click(object sender, RoutedEventArgs e)
    {
        if (_navigation.Current == null) return;
        var dialog = new PrintDialog();
        if (dialog.ShowDialog() == true) dialog.PrintVisual(ImageViewer, _navigation.Current.FileName);
    }

    private void CtxSetWallpaper_Click(object sender, RoutedEventArgs e)
    { if (_navigation.Current != null) WallpaperService.TrySetDesktop(_navigation.Current.FilePath); }
}
