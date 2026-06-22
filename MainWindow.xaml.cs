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
    private readonly NavigationService _navigation = new();
    private readonly SlideshowService _slideshow = new();
    // 8 parallel decodes: roughly matches modern CPU core count; the
    // old default of 2 wasted most of the available IO+decode bandwidth
    // and made folder loads feel sluggish past ~50 items. Cache is
    // resolved from the DI container — see AppHost.Build() in
    // App.OnStartup; the field initializer runs after the host is built.
    private readonly ThumbnailLoadCoordinator _thumbCoordinator = new(
        AppHost.Services!.GetRequiredService<IThumbnailCache>(), maxConcurrent: 8);

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

        // ---- P1: wire up the 9 extracted UserControls ----
        // P2: TitleBarView's events are gone (replaced by VM
        // commands + IUiState). FloatingBar / InfoPill / etc.
        // P2: TitleBar + FloatingBar are VM-driven. The remaining
        // UserControls still raise events for the legacy
        // controllers (which will be replaced by VMs in
        // subsequent P2 commits).
        WireInfoPillEvents();
        WireImageViewerEvents();
        WireTreePanelEvents();
        WireThumbPanelEvents();
        WireEdgeNavEvents();

        // P2: subscribe to IUiState for cross-VM shared state.
        // The column-visibility toggles (tree / thumb) mutate
        // IUiState.IsXxxVisible; MainWindow observes the change
        // and applies the column-width update. The other
        // IUiState flags (IsFullscreen, CurrentImage, etc.) will
        // be observed by other VMs in subsequent commits.
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

        _navigation.CollectionChanged += OnCollectionChanged;
        _navigation.CurrentImageChanged += OnCurrentImageChanged;
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
            if (ReferenceEquals(item, _navigation.Current))
                UpdateCurrentImageInfo(item);
        };
        _navigation.CurrentImageChanged += item =>
        {
            if (item == null) return;
            item.PropertyChanged += (_, _) =>
            {
                if (ReferenceEquals(item, _navigation.Current))
                    Dispatcher.Invoke(() => UpdateCurrentImageInfo(item));
            };
        };

        TreePanelView.FolderTreeRef.FolderSelected += OnFolderSelected;
        TreePanelView.FolderTreeRef.DrillModeChanged += UpdateReturnToRootVisibility;
        ThumbPanelView.ThumbGridRef.ItemClicked += OnThumbClicked;

        _overlayHideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.0) };
        _overlayHideTimer.Tick += (s, e) =>
        {
            if (_isFullscreen) HideEdgeNav();
            _overlayHideTimer?.Stop();
        };

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

        Closed += (_, _) =>
        {
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
        ViewerPanel.ViewerPreviewMouseLeftButtonDown += (_, e) => Viewer_PreviewMouseLeftButtonDown(this, e);

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
        if (ViewerPanel.DataContext is ImageViewerPanelViewModel vm)
            vm.SetViewer(ViewerPanel.ImageViewerRef);
    }

    private void WireEdgeNavEvents()
    {
        if (EdgeNavLeft.DataContext is EdgeNavViewModel leftVm)
            leftVm.NavigateRequested += (_, _) => _navigation.MovePrevious();
        if (EdgeNavRight.DataContext is EdgeNavViewModel rightVm)
            rightVm.NavigateRequested += (_, _) => _navigation.MoveNext();
    }

    private void WireTreePanelEvents()
    {
        TreePanelView.BackRequested += (_, _) => BtnTreeBack_Click(this, new RoutedEventArgs());
        TreePanelView.ReturnToRootRequested += (_, _) => BtnReturnToRoot_Click(this, new RoutedEventArgs());
    }

    private void WireThumbPanelEvents()
    {
        ThumbPanelView.ThumbGridReady += (_, _) => ThumbGrid_Loaded(ThumbPanelView.ThumbGridRef, new RoutedEventArgs());
    }

    // Convenience properties for the controllers (which still
    // reach into XAML elements). These forward to the
    // UserControl property accessors so the controllers can
    // stay self-contained (they don't need to know about the
    // UserControl wrapper layer).

    private System.Windows.Controls.Button BtnOpen => null;  // P2: command-bound; kept null for the legacy ChromeController BtnOpen_Click (still routed via P1)
    private System.Windows.Controls.Button BtnToggleTree => null;  // P2: TitleBar.ToggleTreeColumnCommand drives IUiState.IsTreeVisible
    private System.Windows.Controls.Button BtnToggleThumb => null;  // P2: same pattern
    private System.Windows.Controls.MenuItem MenuPlugins => TitleBar.MenuPluginsRef;
    private System.Windows.Controls.MenuItem MenuAbout => TitleBar.MenuAboutRef;
    private System.Windows.Controls.TextBlock AboutUpdateSuffix => TitleBar.AboutUpdateSuffixRef;
    private Wpf.Ui.Controls.SymbolIcon MaximizeIcon => TitleBar.MaximizeIconRef;
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

    public string? CurrentImagePath => _navigation.Current?.FilePath;

    public IReadOnlyList<string> SelectedImagePaths
        => _navigation.Current == null ? Array.Empty<string>() : new[] { _navigation.Current.FilePath };

    public event EventHandler<string?>? CurrentImageChanged;

    public void RegisterMenuItem(System.Windows.Controls.MenuItem item)
    {
        if (MenuPlugins == null) return;
        MenuPlugins.Items.Add(item);
    }

    public void RegisterContextMenuItem(System.Windows.Controls.MenuItem item)
    {
        var viewer = ViewerPanel?.ImageViewerRef;
        if (viewer?.ContextMenu == null) return;
        var insertBefore = viewer.ContextMenu.Items.OfType<Separator>().LastOrDefault();
        if (insertBefore != null)
            viewer.ContextMenu.Items.Insert(viewer.ContextMenu.Items.IndexOf(insertBefore), item);
        else
            viewer.ContextMenu.Items.Add(item);
    }

    public void UnregisterPluginMenuItems(object pluginTag)
    {
        var menuPlugins = TitleBar?.MenuPluginsRef;
        if (menuPlugins != null)
        {
            for (int i = menuPlugins.Items.Count - 1; i >= 0; i--)
            {
                if (menuPlugins.Items[i] is System.Windows.Controls.MenuItem mi && Equals(mi.Tag, pluginTag))
                    menuPlugins.Items.RemoveAt(i);
            }
        }
        var viewer = ViewerPanel?.ImageViewerRef;
        if (viewer?.ContextMenu != null)
        {
            for (int i = viewer.ContextMenu.Items.Count - 1; i >= 0; i--)
            {
                if (viewer.ContextMenu.Items[i] is System.Windows.Controls.MenuItem mi && Equals(mi.Tag, pluginTag))
                    viewer.ContextMenu.Items.RemoveAt(i);
            }
        }
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
    private static System.Windows.Controls.Grid BuildPluginHeader(PluginInfo info)
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
            item.IsChecked = App.SettingsStore.IsPluginEnabled(info.Name);
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
            if (!App.SettingsStore.IsPluginEnabled(info.Name)) continue;
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
        App.SettingsStore.SetPluginEnabled(info.Name, true);
        // Refresh the dot — status may have transitioned to
        // Enabled (green). SetChecked doesn't fire SubmenuOpened.
        RefreshPluginDot(item, info);
    }

    private void PluginItem_Unchecked(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.MenuItem item) return;
        if (item.Tag is not PluginInfo info) return;
        PluginLoader.Deactivate(info);
        App.SettingsStore.SetPluginEnabled(info.Name, false);
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
    private static void RefreshPluginDot(System.Windows.Controls.MenuItem item, PluginInfo info)
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
    /// Map PluginStatus → the brush for the status dot. Reuses the
    /// design tokens the rest of the app uses for status colors so
    /// the dot visually matches the main interface's status
    /// indicators (info pill dot, status pills, etc.).
    /// </summary>
    private static System.Windows.Media.Brush GetStatusBrush(PluginStatus status)
    {
        var key = status switch
        {
            PluginStatus.Enabled => "StatusGreen",
            PluginStatus.Unavailable => "StatusRed",
            _ => "TextTertiary",  // gray for Disabled
        };
        return (System.Windows.Media.Brush)Application.Current.Resources[key];
    }
}
