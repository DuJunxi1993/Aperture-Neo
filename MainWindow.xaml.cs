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

    private void ToggleFullscreen()
    {
        _isFullscreen = !_isFullscreen;
        if (_isFullscreen) EnterFullscreen();
        else               ExitFullscreen();
    }

    /// <summary>
    /// Enter fullscreen with the simplest possible animation: hide the
    /// viewer area synchronously (Opacity=0, no animation), change the
    /// OS window state, apply the chrome, refit the image synchronously,
    /// then fade the viewer back in over 150ms. The viewer background
    /// does a parallel 150ms ColorAnimation white→black. That's it —
    /// no flash overlays, no delayed FitToScreen, no RenderTransform
    /// scaling. The single Opacity fade hides every layout recompute
    /// and ClientAreaBorder padding change that would otherwise show
    /// as a flicker.
    /// </summary>
    private void EnterFullscreen()
    {
        _prevWindowState = WindowState;
        TransitionToFullscreen(entering: true);
    }

    /// <summary>
    /// Exit fullscreen: fade out the viewer + restore the OS window
    /// in the Completed callback + fade back in. Viewer background
    /// does a parallel black→white animation.
    /// </summary>
    private void ExitFullscreen()
    {
        // Pre-fade hint cleanup. Doing this BEFORE the fadeOut
        // animation (rather than inside the Completed callback) so
        // the exit pill starts its own fade-out in parallel with the
        // viewer's — the user perceives a single coordinated exit.
        HideExitFullscreenHint();
        _exitHintHideTimer?.Stop();

        TransitionToFullscreen(entering: false);
    }

    /// <summary>
    /// Shared fullscreen transition. Mirrors the enter/exit pair so
    /// they stay symmetric: a 150ms ease-in fade-out hides the viewer
    /// (and any layout recompute) from the user, then the OS-level
    /// WindowState/WindowStyle swap + chrome + UpdateLayout +
    /// FitToScreen happens in the Completed callback while the
    /// viewer is still invisible, then a 150ms ease-out fade-in
    /// reveals the new layout. A 150ms background ColorAnimation
    /// runs in parallel so the background is mid-transition when the
    /// fade-in completes. Total 300ms either direction.
    /// </summary>
    private void TransitionToFullscreen(bool entering)
    {
        var targetBackground = entering
            ? (System.Windows.Media.Brush)FindResource("SurfaceBlack")
            : (System.Windows.Media.Brush)FindResource("SurfaceElevated");

        // 1. Fade the viewer to 0 over 150ms (ease-in). After this
        //    completes, swap the OS-level state without the user
        //    seeing any layout recompute.
        var fadeOut = new System.Windows.Media.Animation.DoubleAnimation(
            0d, TimeSpan.FromMilliseconds(150))
        {
            EasingFunction = new System.Windows.Media.Animation.CubicEase
            {
                EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn
            }
        };
        fadeOut.Completed += (_, _) =>
        {
            // 2. OS-level swap. Entering becomes borderless + covers
            //    the whole screen. Exiting forces Normal rather than
            //    _prevWindowState so a previous Maximized state
            //    doesn't accidentally no-op (since the window is
            //    currently Maximized).
            if (entering)
            {
                WindowStyle = WindowStyle.None;
                WindowState = WindowState.Maximized;
            }
            else
            {
                WindowStyle = WindowStyle.SingleBorderWindow;
                WindowState = WindowState.Normal;
            }

            // 3. Apply chrome state synchronously. ApplyColumnVisibility
            //    collapses the side columns; ResetClientAreaBorderPadding
            //    kills WPF-UI's 5px-maximized padding.
            ApplyColumnVisibility();
            UpdateOverlayVisibility();
            ResetClientAreaBorderPadding();

            // 4. Force a synchronous layout pass. WPF layout is async
            //    after WindowState change — without UpdateLayout the
            //    SkiaImageViewer's ActualWidth/ActualHeight still
            //    reflect the pre-transition size, so FitToScreen
            //    would compute centering offsets for the old rect.
            UpdateLayout();

            // 5. Snap the image to the new fit immediately. The viewer
            //    is still at Opacity=0 (we haven't started the
            //    fade-in yet), so the snap is invisible. Skip the
            //    zoom animation because the 200ms slide-in would be
            //    visible through the following 150ms opacity fade.
            ImageViewer.FitToScreenSkipAnimation = true;
            ImageViewer.FitToScreen();

            if (entering)
            {
                // Hide the edge nav and show the exit hint.
                _edgeNavVisible = false;
                EdgeNavLeftContent.Visibility = Visibility.Collapsed;
                EdgeNavRightContent.Visibility = Visibility.Collapsed;
                EdgeNavLeftContent.Opacity = 0;
                EdgeNavRightContent.Opacity = 0;
                _overlayHideTimer?.Stop();
                ShowExitFullscreenHint();
                _exitHintHideTimer?.Stop();
                _exitHintHideTimer?.Start();
            }
            else
            {
                // Re-focus the window so Esc / arrow keys reach the
                // keyboard handler after the HWND swap.
                Focus();

                // If the user had the window Maximized before fullscreen,
                // re-maximize it on the next dispatcher cycle (deferred
                // so it doesn't fight the WindowStyle HWND recreation
                // happening above).
                if (_prevWindowState == WindowState.Maximized)
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        WindowState = WindowState.Maximized;
                    }), System.Windows.Threading.DispatcherPriority.Background);
                }
            }

            // 6. Fade the viewer back in over 150ms (ease-out). The
            //    image appears in its new fit at the new size, on a
            //    background that's mid-transition.
            ViewerColumn.BeginAnimation(UIElement.OpacityProperty,
                new System.Windows.Media.Animation.DoubleAnimation(1d, TimeSpan.FromMilliseconds(150))
                {
                    EasingFunction = new System.Windows.Media.Animation.CubicEase
                    {
                        EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut
                    }
                });
        };
        ViewerColumn.BeginAnimation(UIElement.OpacityProperty, fadeOut);

        // 7. Parallel: viewer background color cross-fade. Started
        //    alongside the fade-out so the background is mid-
        //    transition when the fade-in reveals the new view.
        AnimateViewerBackground(targetBackground, 150);
    }

    /// <summary>
    /// Walks the visual tree looking for a FluentWindow.ClientAreaBorder
    /// (an internal WPF-UI class) and zeroes its Padding. WPF-UI's
    /// OnWindowStateChanged sets Padding to ~5px in Maximized state to
    /// keep the OS's "Aero" border visible — but our viewer is edge-to-
    /// edge, so the ~5px of transparent padding shows the window's
    /// SurfaceCanvas (#fafafa) tone around the white viewer, producing
    /// a 1-2px white-ish seam at every screen edge. We can't reference
    /// the type by name (it's internal), so we match on the class name
    /// in the visual tree and set the public Padding DP inherited from
    /// Border. This is invoked both synchronously inside ToggleFullscreen
    /// (so it beats the FluentWindow padding on the same dispatcher turn)
    /// and on DispatcherPriority.Loaded (catches the case where the
    /// FluentWindow sets padding after we do).
    /// </summary>
    private void ResetClientAreaBorderPadding()
    {
        var cab = VisualTreeHelpers.FindClientAreaBorder(this);
        if (cab == null) return;
        cab.SetValue(System.Windows.Controls.Border.PaddingProperty, new Thickness(0));
    }

    /// <summary>
    /// Goal 3: cross-fade the viewer column background over 200ms.
    /// SolidColorBrush is mutated via ColorAnimation; the brush instance
    /// is kept (so the rest of the visual tree that referenced it
    /// stays valid) and only the underlying color animates.
    /// </summary>
    private void AnimateViewerBackground(System.Windows.Media.Brush target, double durationMs = 200)
    {
        if (target is not System.Windows.Media.SolidColorBrush targetSolid) return;
        // Clone the resource brush so we own the color (resource brushes
        // are shared/frozen; we can't mutate them).
        var current = ViewerColumn.Background as System.Windows.Media.SolidColorBrush;
        if (current == null || current.IsFrozen)
        {
            current = new System.Windows.Media.SolidColorBrush(
                current?.Color ?? System.Windows.Media.Colors.White);
        }
        else
        {
            // Detach the previous animation so the new one wins.
            current.BeginAnimation(System.Windows.Media.SolidColorBrush.ColorProperty, null);
        }
        ViewerColumn.Background = current;
        var anim = new System.Windows.Media.Animation.ColorAnimation
        {
            From = current.Color,
            To = targetSolid.Color,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            EasingFunction = new System.Windows.Media.Animation.CubicEase
            {
                EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut
            }
        };
        current.BeginAnimation(System.Windows.Media.SolidColorBrush.ColorProperty, anim);
    }

    /// <summary>
    /// Double-click anywhere on the viewer (outside the edge-nav
    /// buttons and the exit pill) toggles fullscreen. Uses the
    /// Preview (tunneling) phase so we can mark the event handled
    /// before any bubbling Button.Click fires — that keeps the
    /// double-click from being interpreted as two clicks on the
    /// underlying controls.
    /// </summary>
    /// <summary>
    /// Double-click on the viewer (outside buttons): toggle the
    /// image between Fit-to-screen and 100% (same as the
    /// floating-bar percent label click semantics, but
    /// round-tripping in both directions). Behaviour is the same
    /// in window mode and fullscreen mode — double-click never
    /// exits fullscreen anymore. Esc / Ctrl+F still does.
    /// Preview (tunneling) phase + e.Handled=true so the event
    /// does not bubble to underlying controls (the edge-nav
    /// arrows in fullscreen, the floating bar in window mode).
    /// </summary>
    private void Viewer_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ClickCount < 2) return;
        // Don't trigger on the edge-nav buttons, the exit pill, or
        // the floating bar — those have their own click semantics.
        if (e.OriginalSource is DependencyObject src)
        {
            DependencyObject? walker = src;
            while (walker != null && walker != this)
            {
                if (walker is System.Windows.Controls.Button)
                    return;
                if (walker is System.Windows.Controls.Primitives.ButtonBase)
                    return;
                walker = System.Windows.Media.VisualTreeHelper.GetParent(walker);
            }
        }
        // Toggle between Fit and 100%, in both window mode and
        // fullscreen mode. (Matches the floating-bar percent label
        // click semantics, but in both directions — clicking the
        // percent always zooms to 100%, the viewer double-click
        // rounds-trips fit↔100%.)
        if (ImageViewer.IsAtFitScale)
            ImageViewer.ZoomToOriginal();
        else
            ImageViewer.FitToScreen();
        e.Handled = true;
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
