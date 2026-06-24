using System;
using System.Collections.Generic;
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
using ApertureNeo.ViewModels;
using Microsoft.Extensions.DependencyInjection;
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
public partial class MainWindow : FluentWindow, IPluginContext
{
    // P3: design-token access. Resolved from DI once at
    // construction; the brushes are frozen so they're safe
    // to read from any thread (the fullscreen transition
    // animation tweens the brush's colour on the UI thread,
    // but other call sites might read the same reference).
    private readonly ITheme _theme =
        AppHost.Services!.GetRequiredService<ITheme>();
    // Resolve NavigationService from the DI container so this window
    // shares the same instance the VMs subscribe to. P2 bugfix: the
    // previous `new NavigationService()` created a second, local
    // NavigationService; ImageViewerPanelViewModel subscribed to the
    // DI singleton's CurrentImageChanged, MainWindow raised events on
    // its private instance, so the VM never fired its handler and
    // IUiState.CurrentImage stayed null → InfoPill/InfoPopover never
    // bound to a real ImageItem.
    private readonly INavigationService _navigation =
        AppHost.Services!.GetRequiredService<INavigationService>();
    // SettingsStore + ThumbnailCache + SlideshowService +
    // ThumbnailLoadCoordinator are all DI singletons. The previous
    // App.SettingsStore / App.ThumbnailCache static forwarders were
    // removed in a cleanup pass; these field initializers are the
    // single point of contact between MainWindow and the DI
    // container for non-VM services.
    private readonly ISettingsStore _settings =
        AppHost.Services!.GetRequiredService<ISettingsStore>();
    private readonly IThumbnailCache _thumbnailCache =
        AppHost.Services!.GetRequiredService<IThumbnailCache>();
    private readonly SlideshowService _slideshow =
        AppHost.Services!.GetRequiredService<SlideshowService>();
    // 8 parallel decodes: roughly matches modern CPU core count; the
    // old default of 2 wasted most of the available IO+decode bandwidth
    // and made folder loads feel sluggish past ~50 items. Cache is
    // resolved from the DI container — see AppHost.Build() in
    // App.OnStartup; the field initializer runs after the host is built.
    private readonly ThumbnailLoadCoordinator _thumbCoordinator =
        AppHost.Services!.GetRequiredService<ThumbnailLoadCoordinator>();

    private bool _isTreeVisible = true;
    private bool _isThumbVisible = true;
    // P1: fullscreen state machine + transitions + edge-nav /
    // exit-hint animations + tree floating popup. Owns the
    // transition generation counter, the 3s edge-nav hide timer
    // and 5s exit-hint hide timer, the mouse-position tracking
    // for ShowEdgeNav, and the visual-tree FindClientAreaBorder
    // walk. The controller is constructed right after
    // InitializeComponent (so all XAML element references are
    // resolved) with a FullscreenShell carrying the visual
    // elements + the chrome-update callbacks. MainWindow only
    // routes IUiState.IsFullscreen changes + keyboard / mouse
    // input to it.
    private readonly IFullscreenController _fullscreen;

    // Cached in ThumbGrid_Loaded. Used by ThumbScroller_ScrollChanged to
    // convert a scroll offset/viewport into the actual visible item
    // range (real row height + real column count from the panel's
    // measure pass, not hardcoded guesses).
    private AutoFitPanel? _autoFit;
    // Discovered plugins (set once at startup by App.xaml.cs via
    // SetAvailablePlugins). We hold them so the 插件 submenu can
    // build a checkbox per plugin, and so check/uncheck handlers
    // can call PluginLoader.Activate/Deactivate.
    private IReadOnlyList<PluginInfo> _availablePlugins = Array.Empty<PluginInfo>();

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

        // P1: construct the fullscreen controller after
        // InitializeComponent (so all XAML element references
        // resolve) and after the field initializers above (so
        // _theme is available). The shell carries the visual
        // elements (EdgeNavLeft, EdgeNavRight, ExitHint,
        // ExitHintTransform, ViewerColumn) + the chrome +
        // fit-to-viewer + ClientAreaBorder padding callbacks +
        // the tree floating popup reference. The tree sync
        // callback (FolderTreeFloating.SyncFrom(FolderTree))
        // is wired inline; the popup's Child is the
        // FolderTreeFloating UserControl declared in MainWindow.xaml.
        _fullscreen = new FullscreenController(_theme, new FullscreenShell
        {
            Window = this,
            EdgeNavLeft = EdgeNavLeftControl,
            EdgeNavRight = EdgeNavRightControl,
            ExitHint = ExitFullscreenHintControl,
            ExitHintTransform = ExitFullscreenTransform,
            ViewerColumn = ViewerColumn,
            FitImageToViewer = () =>
            {
                ImageViewer.FitToScreenSkipAnimation = true;
                ImageViewer.FitToScreen();
            },
            ResetClientAreaBorderPadding = ResetClientAreaBorderPadding,
            ApplyChrome = ApplyChrome,
            TreeFloatingPopup = TreeFloatingPopup,
            SyncFloatingTree = () => FolderTreeFloating.SyncFrom(FolderTree),
        });

        // ---- P1: wire up the 9 extracted UserControls ----
        // P2: TitleBarView's events are gone (replaced by VM
        // commands + IUiState). FloatingBar / InfoPill / etc.
        // P2: TitleBar + FloatingBar are VM-driven. The remaining
        // UserControls still raise events for the legacy
        // controllers (which will be replaced by VMs in
        // subsequent P2 commits).
        WireInfoPillEvents();
        WireImageViewerEvents();
        // P2: WireTreePanelEvents is gone. FolderTreePanelView
        // wires FolderTree events into FolderTreePanelVM and
        // VM events into FolderTree method calls. MainWindow
        // only subscribes to FolderNavigationRequested in the
        // event block below (after the IUiState setup).
        WireThumbPanelEvents();
        WireEdgeNavEvents();

        // P2: subscribe to IUiState for cross-VM shared state. The
        // column-visibility toggles (tree / thumb) mutate
        // IUiState.IsXxxVisible; MainWindow observes the change
        // and applies the column-width update. The fullscreen
        // + slideshow flags drive the window-level transition
        // + timer state here (the VMs that set them don't have
        // access to MainWindow's visual tree).
        var uiState = AppHost.Services!.GetRequiredService<IUiState>();
        uiState.PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(IUiState.IsTreeVisible):
                case nameof(IUiState.IsThumbVisible):
                    _isTreeVisible = uiState.IsTreeVisible;
                    _isThumbVisible = uiState.IsThumbVisible;
                    ApplyColumnVisibility();
                    break;
                case nameof(IUiState.IsFullscreen):
                    // P1: FloatingBarViewModel.ToggleFullscreen
                    // (and the Ctrl+F / Esc key handlers via
                    // HandleKey) flip IUiState.IsFullscreen. The
                    // controller runs the actual transition
                    // (entry / exit animation, chrome hide/show,
                    // WPF-UI padding reset, edge-nav show,
                    // exit-hint show, background cross-fade,
                    // image re-fit). MainWindow only routes the
                    // flag change into the controller.
                    // P1 fix: capture the previous WindowState
                    // before toggling so the exit transition can
                    // re-maximize a window that was Maximized
                    // before fullscreen entry.
                    _fullscreen.NotifyEnteringFullscreen();
                    _fullscreen.Toggle();
                    break;
                case nameof(IUiState.IsSlideshowRunning):
                    // P2: FloatingBarViewModel.ToggleSlideshow
                    // flips IUiState.IsSlideshowRunning and updates
                    // its SlideshowIcon; the actual timer is owned
                    // by SlideshowService. Start / Stop based on the
                    // new value so both keyboard (F5 → _slideshow
                    // .Toggle()) and button click paths converge
                    // through the same flag.
                    if (uiState.IsSlideshowRunning) _slideshow.Start();
                    else _slideshow.Stop();
                    break;
            }
        };
        // Keep the local fields in sync with the initial state
        // (so a P2-toggle before any controller runs still works).
        _isTreeVisible = uiState.IsTreeVisible;
        _isThumbVisible = uiState.IsThumbVisible;

        // P2: subscribe to TitleBarViewModel.OpenAboutRequested
        // (raised by the OpenAboutCommand). MainWindow owns the
        // About window's lifetime + the update-suffix feedback.
        if (TitleBar.DataContext is TitleBarViewModel titleVm)
        {
            titleVm.OpenAboutRequested += (_, _) => OpenAboutWindow();
            titleVm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(TitleBarViewModel.IsUpdateAvailable))
                {
                    // Sync back into IUiState so other VMs (e.g.
                    // a future AboutViewModel) can observe the
                    // same flag.
                    uiState.IsUpdateAvailable = titleVm.IsUpdateAvailable;
                }
            };
        }

        // P2: CollectionChanged no longer routes through
        // InfoPopoverController.OnCollectionChanged. The Items
        // binding on the thumb grid already updates via
        // ThumbnailPanelViewModel.Items + PropertyChanged; the
        // only remaining side effect here is the thumb-load
        // coordinator kick (which lives in MainWindow because
        // _thumbCoordinator is per-window, not DI'd).
        _navigation.CollectionChanged += () =>
            _thumbCoordinator.LoadForFolder(_navigation.Items, _navigation.CurrentIndex, null);
        // P2: CurrentImageChanged no longer routes through
        // InfoPopoverController.OnCurrentImageChanged. The thumb
        // grid's SelectedItem binding + the View's scroll-into-view
        // are VM-driven; the remaining side effects here are
        // window-level (title text + viewer context-menu close).
        _navigation.CurrentImageChanged += item =>
        {
            if (item == null) return;
            Title = $"Aperture Neo · {item.FileName} ({_navigation.CurrentIndex + 1}/{_navigation.Count})";
            if (ImageViewer.ContextMenu != null) ImageViewer.ContextMenu.IsOpen = false;
        };
        _navigation.CurrentImageChanged += item => CurrentImageChanged?.Invoke(this, item?.FilePath);
        _slideshow.NextRequested += () => Dispatcher.Invoke(() => _navigation.MoveNext());

        // P2: the ZoomText and ImageIndexInfo text blocks are
        // bound to FloatingBarViewModel properties. The VM
        // subscribes to ImageViewer.ZoomChanged and
        // NavigationService events and updates those properties
        // — no MainWindow wiring needed here. ImageLoaded still
        // needs MainWindow's reach (it updates ImageItem
        // dimensions + the info pill).
        ViewerPanel.ImageViewerRef.ImageLoaded += result =>
        {
            var item = _navigation.Items.FirstOrDefault(i => i.FilePath == result.FilePath);
            if (item == null) return;
            item.SetDimensions(result.Width, result.Height);
        };

        // P2: FolderTreePanelVM now owns the folder-selection
        // side effects (recent-add + FolderNavigationRequested
        // event). The View forwards FolderTree.FolderSelected
        // into the VM; MainWindow subscribes to FolderNavigation
        // Requested and calls NavigationService.LoadFolder.
        // Drill-mode visibility is bound to FolderTreePanelVM
        // .IsDrillMode via XAML; DrillModeChanged wiring moved
        // out of MainWindow along with the dead UpdateReturnTo
        // RootVisibility controller method.
        if (TreePanelView.DataContext is FolderTreePanelViewModel treeVm)
        {
            treeVm.FolderNavigationRequested += (_, path) => _navigation.LoadFolder(path);
        }
        // P2: ThumbGrid.ItemClicked is now wired by
        // ThumbnailPanelView directly to ThumbnailPanelViewModel
        // .ThumbClickedCommand (the VM calls NavigationService
        // .NavigateTo). MainWindow no longer needs to know about
        // the click event.

        // P1 fix: the overlay-hide timers (3s edge nav, 5s exit
        // hint, 250ms tree popup) used to live here and were
        // tick-handled by inline lambdas that closed over
        // MainWindow. The lambdas held MainWindow alive past
        // process exit if the window closed mid-animation.
        // They're now owned by FullscreenController (created
        // in its ctor), and Detach() stops them on window
        // close. The controller also owns the mouse-position
        // tracking + edge-nav visibility state.
        // P1: App.OnStartup re-saves the LastOpenedImage on
        // exit, but the field is also written here so a crash
        // doesn't lose the position. The previous P0 fix kept
        // HideEdgeNav / HideExitFullscreenHint + timer Stop()
        // calls in the Closed handler; those are now in
        // _fullscreen.Detach() (called below).

        Loaded += (_, _) =>
        {
            Focus();
            Dispatcher.BeginInvoke(new Action(() =>
            {
                UpdateOverlayVisibility();

                var recent = _settings.Recent;
                if (recent.Count > 0 && Directory.Exists(recent[0].Path))
                {
                    _navigation.LoadFolder(recent[0].Path);
                }
            }), DispatcherPriority.Loaded);
        };

        Closed += (_, _) =>
        {
            var current = _navigation.Current;
            if (current != null)
                _settings.LastOpenedImage = current.FilePath;

            // P1: fullscreen state machine cleanup (timers,
            // in-flight fade animations). The controller's
            // Detach cancels its BeginAnimation calls so the
            // Completed lambdas don't fire after detach and
            // capture this MainWindow via the closure.
            _fullscreen.Detach();
            // Defensive: kill any cross-fade the SkiaImageViewer
            // might still be running so its OnRendering handler
            // (which also captures this MainWindow via the lambda)
            // is detached.
            ImageViewer?.AbortAnimations();

            _thumbCoordinator.Dispose();
            _slideshow.Dispose();
            (_navigation as IDisposable)?.Dispose();
        };

        PreviewKeyDown += (_, e) => { if (HandleKey(e.Key)) e.Handled = true; };
        // P1: forward MouseMove to the fullscreen controller
        // (which decides whether to show the edge-nav buttons
        // based on the 5px-jitter threshold and restarts the 3s
        // auto-hide timer). The controller also owns the
        // last-mouse-position field, so MainWindow no longer
        // carries _lastMousePosition.
        MouseMove += (_, e) => _fullscreen.OnMouseMove(e.GetPosition(this));
        Drop += OnWindowDrop;
    }

    // ---- P1: UserControl event wiring ----
    // P2: TitleBar + FloatingBar are now VM-driven (their P1
    // events are gone; the XAML binds to VM commands).
    // WireXxxEvents for those two are removed; the remaining
    // UserControls still raise events for the controllers
    // (which will be replaced by VMs in subsequent P2 commits).

    private void OpenAboutWindow()
    {
        var win = new AboutWindow(this);
        var uiState = AppHost.Services!.GetRequiredService<IUiState>();
        win.UpdateAvailableChanged += (_, available) => uiState.IsUpdateAvailable = available;
        win.ShowDialog();
    }

    // P2: FloatingBarView's events are gone (replaced by
    // VM commands + IUiState). The slideshow + fullscreen
    // toggles mutate IUiState.IsSlideshowRunning / IsFullscreen;
    // MainWindow observes those flags and runs the actual
    // timers / state machine. The legacy WireFloatingBarEvents
    // method is removed; controller methods that depended on
    // it (BtnPrev_Click, BtnNext_Click, BtnFit_Click,
    // BtnSlideshow_Click, BtnFullscreen_Click, ZoomTextBlock_Click)
    // are now dead code and will be removed in the controller
    // sweep at the end of P2.

    private void WireInfoPillEvents()
    {
        // P2: InfoPillView binds to InfoPillViewModel which raises
        // PopoverToggleRequested on click. We open / close the
        // popover here (the View raises the event via the VM's
        // ToggleCommand, which goes through the binding chain).
        if (InfoPill.DataContext is InfoPillViewModel pillVm)
        {
            pillVm.PopoverToggleRequested += (_, _) =>
            {
                if (InfoPopover.IsOpen)
                {
                    InfoPopover.IsOpen = false;
                    return;
                }
                // Popover body is bound to InfoPopoverViewModel;
                // calling its Refresh command re-reads the
                // current image's metadata.
                if (InfoPopoverBody.DataContext is InfoPopoverViewModel popVm)
                    popVm.Refresh();
                InfoPopover.StaysOpen = true;
                InfoPopover.HorizontalOffset = 0;
                InfoPopover.IsOpen = true;
                Dispatcher.BeginInvoke(new Action(AlignPopoverToPillRight), DispatcherPriority.Loaded);
            };
        }
    }

    private void WireImageViewerEvents()
    {
        ViewerPanel.CopyPathRequested += (_, _) => CtxCopyPath_Click(this, new RoutedEventArgs());
        ViewerPanel.OpenInExplorerRequested += (_, _) => CtxOpenInExplorer_Click(this, new RoutedEventArgs());
        ViewerPanel.PrintRequested += (_, _) => CtxPrint_Click(this, new RoutedEventArgs());
        ViewerPanel.SetWallpaperRequested += (_, _) => CtxSetWallpaper_Click(this, new RoutedEventArgs());
        // P2: Viewer_PreviewMouseLeftButtonDown is gone. The
        // viewer double-click fit↔zoom toggle is owned by
        // ImageViewerPanelViewModel (SetViewer hooks the
        // viewer's PreviewMouseLeftButtonDown directly).

        // P2: ImageViewerPanelViewModel.SetViewer needs the
        // SkiaImageViewer reference after the View's Loaded.
        // Wire this in MainWindow because the viewer is created
        // by MainWindow.xaml, not by the View.
        if (ViewerPanel.IsLoaded)
            WireImageViewerPanelVM();
        else
            ViewerPanel.Loaded += (_, _) => WireImageViewerPanelVM();
    }

    private void WireImageViewerPanelVM()
    {
        // P1: both the image-viewer VM (handles double-click fit↔zoom
        // toggle) and the floating-bar VM (Fit / 100% buttons) need
        // the SkiaImageViewer reference. Hand the same instance to
        // both so they target the same viewer the user is looking at.
        if (ViewerPanel.DataContext is ImageViewerPanelViewModel vm)
            vm.SetViewer(ViewerPanel.ImageViewerRef);
        if (FloatingBar.DataContext is FloatingBarViewModel barVm)
            barVm.SetViewer(ViewerPanel.ImageViewerRef);
    }

    private void WireEdgeNavEvents()
    {
        if (EdgeNavLeft.DataContext is EdgeNavViewModel leftVm)
            leftVm.NavigateRequested += (_, _) => _navigation.MovePrevious();
        if (EdgeNavRight.DataContext is EdgeNavViewModel rightVm)
            rightVm.NavigateRequested += (_, _) => _navigation.MoveNext();
    }

    // P2: WireTreePanelEvents is gone. FolderTreePanelView
    // forwards FolderTree events into FolderTreePanelVM and
    // VM Back/ReturnToRoot events into FolderTree method
    // calls. MainWindow only subscribes to FolderNavigation
    // Requested (see Loaded-time wiring below) and calls
    // NavigationService.LoadFolder(path).

    private void WireThumbPanelEvents()
    {
        // P2: ThumbGrid_Loaded (the inner-ScrollViewer / AutoFitPanel
        // hookup) and ThumbScroller_ScrollChanged (visible-range
        // notify) live as inline methods on MainWindow now; the
        // controller partials no longer carry this code. The wiring
        // here waits for ThumbnailPanelView to re-raise its own
        // ThumbGrid.Loaded event (it doesn't have access to
        // _thumbCoordinator so the visual-tree walk + size-provider
        // setup has to happen in MainWindow).
        ThumbPanelView.ThumbGridReady += (_, _) => OnThumbGridReady(ThumbPanelView.ThumbGridRef);
    }

    private void OnThumbGridReady(ThumbnailGrid grid)
    {
        // Inner ScrollViewer isn't reachable from XAML — ThumbnailGrid
        // builds it lazily from its template. Hook Loaded on the grid,
        // walk down to the first ScrollViewer descendant, and
        // subscribe ScrollChanged so we can drive the visible-range
        // thumbnail loader.
        var innerScroller = VisualTreeHelpers.FindVisualChild<ScrollViewer>(grid);
        if (innerScroller == null) return;
        innerScroller.ScrollChanged += (_, e) => OnThumbScrollerScrollChanged(grid, e);

        // AutoFitPanel is the ItemsPanelTemplate host — it knows the
        // real per-cell width. Hand the cache a live size provider so
        // every new thumbnail is generated at the correct resolution
        // (the cache was constructed with a default 256px size; this
        // replaces it with the live measurement).
        var autoFit = VisualTreeHelpers.FindVisualChild<AutoFitPanel>(grid);
        if (autoFit != null)
        {
            _thumbnailCache.SetSizeProvider(() => (int)autoFit.ActualItemWidth);
            _autoFit = autoFit;
        }
    }

    private void OnThumbScrollerScrollChanged(ThumbnailGrid grid, ScrollChangedEventArgs e)
    {
        if (_navigation.Count == 0 || _autoFit == null) return;
        var (firstIdx, lastIdx) = _autoFit.GetVisibleIndexRange(
            e.VerticalOffset, e.ViewportHeight, _navigation.Count);
        if (firstIdx < 0) return;
        _thumbCoordinator.EnsureVisible(firstIdx, lastIdx);
    }

    // P1: callbacks passed to FullscreenController via the
    // FullscreenShell. They run on the UI thread inside the
    // transition's Completed callback (after WindowState has
    // been swapped) so the visual state is consistent by the
    // time the viewer's opacity fade-in starts.

    /// <summary>Walk the visual tree looking for the WPF-UI
    /// FluentWindow.ClientAreaBorder (an internal class; we
    /// match by name) and zero its Padding. WPF-UI's
    /// OnWindowStateChanged sets Padding to ~5px in Maximized
    /// state to keep the OS's "Aero" border visible — but our
    /// viewer is edge-to-edge, so the 5px of transparent
    /// padding shows the window's SurfaceCanvas tone around
    /// the white viewer, producing a 1-2px seam at every
    /// screen edge. Called both synchronously in the
    /// transition (so it beats the FluentWindow padding on the
    /// same dispatcher turn) and at Loaded priority
    /// (catches the case where FluentWindow sets padding
    /// after we do).</summary>
    private void ResetClientAreaBorderPadding()
    {
        var cab = VisualTreeHelpers.FindClientAreaBorder(this);
        if (cab == null) return;
        cab.SetValue(System.Windows.Controls.Border.PaddingProperty, new Thickness(0));
    }

    /// <summary>Chrome update wrapper. The fullscreen
    /// controller dispatches to this after the OS-level
    /// WindowState swap (so the chrome collapses / re-shows
    /// in sync with the window change). MainWindow owns the
    /// actual logic — it's the only class that has the column
    /// definitions + the title-bar / floating-bar / info-pill
    /// forwarding properties.</summary>
    private void ApplyChrome() => UpdateOverlayVisibility();

    // Convenience properties for the controllers (which still
    // reach into XAML elements). These forward to the
    // UserControl property accessors so the controllers can
    // stay self-contained (they don't need to know about the
    // UserControl wrapper layer).

    private System.Windows.Controls.MenuItem MenuPlugins => TitleBar.MenuPluginsRef;
    // P2: SlideshowIcon / ImageIndexInfo / ZoomTextBlock are
    // now VM properties (bound via XAML on FloatingBarView).
    // The legacy convenience properties are removed; the
    // controllers that referenced them (ChromeController,
    // InfoPopoverController, FullscreenController) will be
    // deleted at the end of P2 along with this block.
    private System.Windows.Controls.Border TitleBarArea => TitleBar.TitleBarAreaRef;
    private System.Windows.Controls.Border FloatingBarContent => FloatingBar.FloatingBarContentRef;
    private System.Windows.Controls.Border InfoPillContent => InfoPill.InfoPillContentRef;
    private System.Windows.Controls.Border InfoPillDot => InfoPill.InfoPillDotRef;
    // P2: ImageInfo's Text is bound to InfoPillViewModel.ImageInfo;
    // the controller no longer reaches in directly.

    // P1 fix: EdgeNav + ExitFullscreenHint UserControls have
    // Opacity="0" set in MainWindow.xaml (so they don't flash
    // on the first frame before the show animation runs). The
    // WPF compositor multiplies the parent Opacity by the
    // child Opacity, so animating the inner Border's Opacity
    // (the old EdgeNavLeftContent / ExitFullscreenHint
    // properties) was a no-op — the UserControl's parent
    // Opacity=0 forced the effective Opacity to 0 regardless
    // of what the Border did. We now expose the UserControls
    // themselves and animate / read their Opacity directly.
    private UserControl EdgeNavLeftControl => EdgeNavLeft;
    private UserControl EdgeNavRightControl => EdgeNavRight;
    private UserControl ExitFullscreenHintControl => ExitFullscreenHintView;

    // IsMouseOver still needs to read the inner Border (the
    // hit-test surface) — a UserControl's IsMouseOver only
    // returns true when the cursor is over a non-empty part
    // of the UserControl's own bounds, which on an empty
    // UserControl wrapper is unreliable. The Border inside
    // is the actual hit-test target.
    private System.Windows.Controls.Border EdgeNavLeftContent => EdgeNavLeft.EdgeNavBorderRef;
    private System.Windows.Controls.Border EdgeNavRightContent => EdgeNavRight.EdgeNavBorderRef;
    private System.Windows.Controls.Border ExitFullscreenHint => ExitFullscreenHintView.HintBorderRef;
    private System.Windows.Media.TranslateTransform ExitFullscreenTransform => ExitFullscreenHintView.TransformRef;
    private System.Windows.Controls.Grid ViewerColumn => ViewerPanel.ViewerColumnRef;
    private ApertureNeo.Controls.SkiaImageViewer ImageViewer => ViewerPanel.ImageViewerRef;
    private ApertureNeo.Controls.FolderTree.FolderTreeView FolderTree => TreePanelView.FolderTreeRef;
    private System.Windows.Controls.Button BtnTreeBack => TreePanelView.BtnTreeBackRef;
    private System.Windows.Controls.Button BtnReturnToRoot => TreePanelView.BtnReturnToRootRef;
    private ApertureNeo.Controls.ThumbnailGrid ThumbGrid => ThumbPanelView.ThumbGridRef;
    private System.Windows.Controls.StackPanel ThumbEmpty => ThumbPanelView.ThumbEmptyRef;

    public MainWindow(string filePath) : this()
    {
        if (File.Exists(filePath))
        {
            var folder = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
            {
                if (FormatHelper.FolderHasImages(folder))
                    _settings.AddRecent(folder);
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

    public string? CurrentImagePath => _navigation.Current?.FilePath;

    public IReadOnlyList<string> SelectedImagePaths
        => _navigation.Current == null ? Array.Empty<string>() : new[] { _navigation.Current.FilePath };

    public event EventHandler<string?>? CurrentImageChanged;

    // P4: ViewSlot routes plugin UI to the named shell region.
    // Replaces the old RegisterMenuItem / RegisterContextMenuItem /
    // UnregisterPluginMenuItems methods — plugins now push UI
    // generically (any UIElement, not just MenuItem) and identify
    // their own contributions via Tag = this for cleanup.
    public void ViewSlot(string region, object content)
    {
        switch (region)
        {
            case ShellRegions.TitleBarMenu:
                AddToTitleBarMenu(content);
                break;
            case ShellRegions.ViewerContextMenu:
                AddToViewerContextMenu(content);
                break;
            default:
                DebugLog.Write("Plugin", $"unknown region '{region}'; content ignored");
                break;
        }
    }

    /// <summary>Remove every contribution tagged with
    /// <paramref name="pluginTag"/> from every wired region.</summary>
    public void ClearSlots(object pluginTag)
    {
        RemoveTaggedFrom(MenuPlugins?.Items, pluginTag);
        var viewer = ViewerPanel?.ImageViewerRef;
        if (viewer?.ContextMenu != null)
            RemoveTaggedFrom(viewer.ContextMenu.Items, pluginTag);
    }

    private static void RemoveTaggedFrom(System.Windows.Controls.ItemCollection? items, object? pluginTag)
    {
        if (items == null) return;
        for (int i = items.Count - 1; i >= 0; i--)
        {
            if (Equals(items[i] is System.Windows.FrameworkElement fe ? fe.Tag : null, pluginTag))
                items.RemoveAt(i);
        }
    }

    private void AddToTitleBarMenu(object content)
    {
        if (MenuPlugins == null) return;
        // Idempotent: if the same plugin re-Activates, replace the
        // existing item with the same Tag rather than appending a
        // duplicate. Plugin authors set Tag = this per the IPluginContext
        // contract, so any existing contribution from this plugin is
        // safe to remove before insertion.
        RemoveTaggedFrom(MenuPlugins.Items, GetPluginTag(content));
        MenuPlugins.Items.Add(content);
    }

    private void AddToViewerContextMenu(object content)
    {
        var viewer = ViewerPanel?.ImageViewerRef;
        if (viewer?.ContextMenu == null) return;
        // Idempotent: drop any existing contribution from this plugin
        // before inserting the new one (see AddToTitleBarMenu).
        RemoveTaggedFrom(viewer.ContextMenu.Items, GetPluginTag(content));
        // Insert before the last Separator so plugin items group
        // together at the bottom of the menu, matching the layout
        // the old RegisterContextMenuItem produced.
        var insertBefore = viewer.ContextMenu.Items.OfType<Separator>().LastOrDefault();
        if (insertBefore != null)
            viewer.ContextMenu.Items.Insert(viewer.ContextMenu.Items.IndexOf(insertBefore), content);
        else
            viewer.ContextMenu.Items.Add(content);
    }

    private static object? GetPluginTag(object content)
    {
        // Plugins are expected to set FrameworkElement.Tag to their
        // own IPluginModule instance per the IPluginContext contract.
        // Read it here so AddToTitleBarMenu / AddToViewerContextMenu
        // can dedup by-tag without the plugin having to track its
        // own slot list.
        if (content is System.Windows.FrameworkElement fe) return fe.Tag;
        return null;
    }

    /// <summary>
    /// Called by App.xaml.cs after PluginLoader.Discover. Builds a
    /// checkbox MenuItem per discovered plugin in the 插件 submenu.
    /// Plugins are not activated yet — the user has to check the
    /// box (or the box is auto-checked by RestoreEnabledPlugins if
    /// the plugin was enabled in a previous session).
    /// </summary>
    public void SetAvailablePlugins(IReadOnlyList<PluginInfo> plugins)
    {
        _availablePlugins = plugins;
        if (MenuPlugins == null) return;

        // Clear any existing items (e.g. leftover from a previous
        // call in a test scenario).
        MenuPlugins.Items.Clear();

        // Sync checkbox visuals when the submenu is first opened.
        // WPF ContextMenus don't realize their child items until
        // the menu is shown, so we can't set IsChecked in
        // SetAvailablePlugins/RestoreEnabledPlugins — the property
        // setter is a no-op on an unrealized MenuItem. Instead we
        // hook SubmenuOpened and apply the checked state from the
        // SettingsStore at that point. The same handler also
        // refreshes the status dot so any change since last open
        // (e.g. model files copied in) is reflected immediately.
        MenuPlugins.SubmenuOpened += MenuPlugins_SubmenuOpened;

        foreach (var info in plugins)
        {
            var item = new System.Windows.Controls.MenuItem
            {
                // Build the header as a horizontal Grid: [dot] [name].
                // We don't use MenuItem.Icon for the dot because
                // WPF's default MenuItem template wraps non-Image
                // icons in a ContentPresenter that doesn't size the
                // child correctly — the dot either doesn't render
                // or gets stretched. A Grid header sidesteps this
                // entirely and lays out the dot + text exactly the
                // way we want.
                Header = BuildPluginHeader(info),
                IsCheckable = true,
                // Greyed-out checkbox when the plugin can't run
                // (e.g. missing ONNX model files). The user can
                // still see what's installed but can't toggle it on
                // until the missing files are restored.
                IsEnabled = info.Instance.Status != PluginStatus.Unavailable,
                Tag = info,
            };
            item.Checked += PluginItem_Checked;
            item.Unchecked += PluginItem_Unchecked;
            MenuPlugins.Items.Add(item);
        }
    }

    /// <summary>
    /// Build the [dot] [plugin name] header layout for a plugin
    /// toggle. The dot is stored as the Border's Tag so
    /// SubmenuOpened / Checked / Unchecked can find it and swap the
    /// Background brush when the status changes.
    /// </summary>
    // P3: was static; now instance because GetStatusBrush is
    // instance (it reads the per-window _theme field).
    private System.Windows.Controls.Grid BuildPluginHeader(PluginInfo info)
    {
        var grid = new System.Windows.Controls.Grid();
        grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = System.Windows.GridLength.Auto });
        grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });

        // 8×8 dot with 4 corner-radius = circle. Same proportions
        // as the LinearDot style in Styles/Tag.xaml.
        var dot = new System.Windows.Controls.Border
        {
            Width = 8,
            Height = 8,
            CornerRadius = new System.Windows.CornerRadius(4),
            Background = GetStatusBrush(info.Instance.Status),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new System.Windows.Thickness(0, 0, 8, 0),
            // Tag the dot so refresh handlers can find it.
            Tag = "PluginStatusDot",
        };
        System.Windows.Controls.Grid.SetColumn(dot, 0);
        grid.Children.Add(dot);

        var text = new System.Windows.Controls.TextBlock
        {
            Text = info.Name,
            VerticalAlignment = VerticalAlignment.Center,
        };
        System.Windows.Controls.Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        return grid;
    }

    /// <summary>
    /// When the 插件 submenu is first opened, sync each plugin
    /// toggle's IsChecked with the persisted SettingsStore state
    /// and refresh the status dot (which can change between opens
    /// — e.g. the user copied model files in, or the warmup
    /// finished). WPF ContextMenus don't realize their child
    /// MenuItems until the menu is shown, so this is the first
    /// point where the IsChecked setter actually takes effect.
    /// </summary>
    private void MenuPlugins_SubmenuOpened(object sender, RoutedEventArgs e)
    {
        if (MenuPlugins == null) return;
        foreach (var item in MenuPlugins.Items.OfType<System.Windows.Controls.MenuItem>())
        {
            if (item.Tag is not PluginInfo info) continue;
            // IsChecked setter suppresses re-entrancy if the value
            // doesn't change, so calling this on every open is safe
            // and cheap.
            item.IsChecked = _settings.IsPluginEnabled(info.Name);
            RefreshPluginDot(item, info);
            item.IsEnabled = info.Instance.Status != PluginStatus.Unavailable;
        }
    }

    /// <summary>
    /// Read the persisted enabled-plugins list from SettingsStore and
    /// activate the previously-enabled plugins. The visual checkbox
    /// state is synced lazily by MenuPlugins_SubmenuOpened when the
    /// user first opens the menu.
    /// </summary>
    public void RestoreEnabledPlugins()
    {
        foreach (var info in _availablePlugins)
        {
            if (!_settings.IsPluginEnabled(info.Name)) continue;
            // Activate the plugin directly. We can't set IsChecked
            // here (the ContextMenu is unrealized, so the setter is
            // a no-op) — the visual state gets applied in
            // MenuPlugins_SubmenuOpened.
            PluginLoader.Activate(info, this);
        }
    }

    private void PluginItem_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.MenuItem item) return;
        if (item.Tag is not PluginInfo info) return;
        PluginLoader.Activate(info, this);
        _settings.SetPluginEnabled(info.Name, true);
        // Refresh the dot — status may have transitioned to
        // Enabled (green). SetChecked doesn't fire SubmenuOpened.
        RefreshPluginDot(item, info);
    }

    private void PluginItem_Unchecked(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.MenuItem item) return;
        if (item.Tag is not PluginInfo info) return;
        PluginLoader.Deactivate(info);
        _settings.SetPluginEnabled(info.Name, false);
        // Refresh the dot — status transitioned back to
        // Disabled (gray). SetChecked doesn't fire SubmenuOpened.
        RefreshPluginDot(item, info);
    }

    /// <summary>
    /// Find the status dot inside the item's Grid header and update
    /// its Background brush. The dot is tagged "PluginStatusDot" in
    /// BuildPluginHeader so we can find it without walking every
    /// Border descendant.
    /// </summary>
    // P3: was static; now instance because GetStatusBrush is
    // instance (reads the per-window _theme field).
    private void RefreshPluginDot(System.Windows.Controls.MenuItem item, PluginInfo info)
    {
        if (item.Header is not System.Windows.Controls.Grid grid) return;
        foreach (var child in grid.Children)
        {
            if (child is System.Windows.Controls.Border dot
                && Equals(dot.Tag, "PluginStatusDot"))
            {
                dot.Background = GetStatusBrush(info.Instance.Status);
                return;
            }
        }
    }

    /// <summary>
    /// P3: maps PluginStatus → a brush via <see cref="ITheme"/>.
    /// Was a static method that fished the brush out of
    /// <c>Application.Current.Resources</c> by string key;
    /// now an instance method on MainWindow because the
    /// theme is per-instance (DI-resolved singleton, but
    /// we still need the instance reference). Reuses the
    /// design tokens the rest of the app uses for status colors
    /// so the dot visually matches the main interface's status
    /// indicators (info pill dot, status pills, etc.).
    /// </summary>
    private System.Windows.Media.Brush GetStatusBrush(PluginStatus status)
    {
        return status switch
        {
            PluginStatus.Enabled => _theme.StatusGreen,
            PluginStatus.Unavailable => _theme.StatusRed,
            _ => _theme.TextTertiary,  // gray for Disabled
        };
    }
}
