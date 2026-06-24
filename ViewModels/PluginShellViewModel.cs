using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using ApertureNeo.Helpers;
using ApertureNeo.Services;

namespace ApertureNeo.ViewModels;

/// <summary>
/// Plugin shell: implements <see cref="IPluginContext"/> for
/// the rest of the app to hand to plugins via
/// <c>PluginLoader.Activate(info, pluginShell)</c>. Owns the
/// 插件 submenu's MenuItem collection (added by
/// <see cref="SetAvailablePlugins"/>), the per-plugin
/// checkbox + status dot rendering, and the viewer's
/// right-click context menu (where plugins add items via
/// <see cref="ViewSlot(string, object)"/>).
///
/// P1: extracted from MainWindow.xaml.cs (200+ lines of
/// ViewSlot / ClearSlots / AddToTitleBarMenu / menu item
/// construction / status dot refresh / GetStatusBrush
/// / SetAvailablePlugins / RestoreEnabledPlugins). The VM
/// holds the same IPluginContext contract so the existing
/// OCR plugin (which depends on IPluginContext) keeps
/// working unchanged. MainWindow stops implementing
/// IPluginContext; it only exposes the VM as an
/// <c>IPluginContext</c> via a property, and App.RunViewer
/// Async forwards the VM to <c>PluginLoader.Activate</c>.
///
/// Visual elements are passed in lazily via
/// <see cref="AttachShell"/> (right after the host
/// MainWindow's InitializeComponent, so the XAML
/// element references are resolved). The viewer
/// ContextMenu is read from the SkiaImageViewer's
/// ContextMenu (set in ImageViewerPanelView.xaml — the
/// 4 default items are: CopyPath / OpenInExplorer / Print
/// / SetWallpaper).
/// </summary>
public partial class PluginShellViewModel : ObservableObject, IPluginContext
{
    private readonly ISettingsStore _settings;
    private readonly ITheme _theme;
    private readonly INavigationService _navigation;

    /// <summary>The 插件 submenu in the title bar's overflow
    /// menu. Set by <see cref="AttachShell"/> right after
    /// MainWindow's InitializeComponent.</summary>
    private MenuItem? _menuPlugins;

    /// <summary>The SkiaImageViewer's right-click context
    /// menu (declared in ImageViewerPanelView.xaml). Set by
    /// <see cref="AttachShell"/>. Plugins' <c>ViewSlot(
    /// ShellRegions.ViewerContextMenu, item)</c> add their
    /// menu items here.</summary>
    private ContextMenu? _viewerContextMenu;

    /// <summary>Discovered plugins, set by
    /// <see cref="SetAvailablePlugins"/>. Held so
    /// <see cref="RestoreEnabledPlugins"/> can iterate and
    /// re-activate the ones the user previously enabled.</summary>
    private IReadOnlyList<PluginInfo> _availablePlugins = Array.Empty<PluginInfo>();

    public PluginShellViewModel(ISettingsStore settings, ITheme theme, INavigationService navigation)
    {
        _settings = settings;
        _theme = theme;
        _navigation = navigation;
        // Re-raise navigation events as IPluginContext events
        // so plugins (notably OCR's "pre-decode the current
        // image" warmup) observe the same navigation the
        // ViewModels do.
        _navigation.CurrentImageChanged += item => CurrentImageChanged?.Invoke(this, item?.FilePath);
    }

    /// <summary>Connect the visual elements. Call once from
    /// MainWindow's ctor after InitializeComponent has
    /// resolved the XAML references.</summary>
    public void AttachShell(MenuItem menuPlugins, ContextMenu viewerContextMenu)
    {
        _menuPlugins = menuPlugins;
        _viewerContextMenu = viewerContextMenu;
    }

    // ---- IPluginContext ----

    public string? CurrentImagePath => _navigation.Current?.FilePath;
    public IReadOnlyList<string> SelectedImagePaths
        => _navigation.Current == null ? Array.Empty<string>() : new[] { _navigation.Current.FilePath };
    public event EventHandler<string?>? CurrentImageChanged;

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

    public void ClearSlots(object pluginTag)
    {
        if (_menuPlugins != null)
            RemoveTaggedFrom(_menuPlugins.Items, pluginTag);
        if (_viewerContextMenu != null)
            RemoveTaggedFrom(_viewerContextMenu.Items, pluginTag);
    }

    // ---- SetAvailablePlugins / RestoreEnabledPlugins ----

    /// <summary>Called by App.RunViewerAsync after
    /// <c>PluginLoader.Discover</c>. Builds one checkbox
    /// MenuItem per plugin in the 插件 submenu. The actual
    /// activate / deactivate is deferred to
    /// <see cref="RestoreEnabledPlugins"/> so the user's
    /// persisted enabled state is honoured.</summary>
    public void SetAvailablePlugins(IReadOnlyList<PluginInfo> plugins)
    {
        _availablePlugins = plugins;
        if (_menuPlugins == null) return;

        // Clear any existing items (e.g. leftover from a
        // previous call in a test scenario).
        _menuPlugins.Items.Clear();

        // Sync checkbox visuals when the submenu is first
        // opened. WPF ContextMenus don't realize their child
        // items until the menu is shown, so we can't set
        // IsChecked in SetAvailablePlugins — the property
        // setter is a no-op on an unrealized MenuItem.
        // Instead we hook SubmenuOpened and apply the
        // checked state from the SettingsStore at that
        // point. The same handler also refreshes the status
        // dot so any change since last open (e.g. model
        // files copied in) is reflected immediately.
        _menuPlugins.SubmenuOpened += MenuPlugins_SubmenuOpened;

        foreach (var info in plugins)
        {
            var item = new MenuItem
            {
                // Build the header as a horizontal Grid:
                // [dot] [name]. We don't use MenuItem.Icon for
                // the dot because WPF's default MenuItem
                // template wraps non-Image icons in a
                // ContentPresenter that doesn't size the child
                // correctly — the dot either doesn't render or
                // gets stretched. A Grid header sidesteps this
                // entirely and lays out the dot + text exactly
                // the way we want.
                Header = BuildPluginHeader(info),
                IsCheckable = true,
                // Greyed-out checkbox when the plugin can't
                // run (e.g. missing ONNX model files). The
                // user can still see what's installed but can't
                // toggle it on until the missing files are
                // restored.
                IsEnabled = info.Instance.Status != PluginStatus.Unavailable,
                Tag = info,
            };
            item.Checked += PluginItem_Checked;
            item.Unchecked += PluginItem_Unchecked;
            _menuPlugins.Items.Add(item);
        }
    }

    /// <summary>Read the persisted enabled-plugins list
    /// from <see cref="ISettingsStore"/> and activate the
    /// previously-enabled plugins. The visual checkbox
    /// state is synced lazily by MenuPlugins_SubmenuOpened
    /// when the user first opens the menu.</summary>
    public void RestoreEnabledPlugins()
    {
        foreach (var info in _availablePlugins)
        {
            if (!_settings.IsPluginEnabled(info.Name)) continue;
            // Activate the plugin directly. We can't set
            // IsChecked here (the ContextMenu is unrealized,
            // so the setter is a no-op) — the visual state
            // gets applied in MenuPlugins_SubmenuOpened.
            PluginLoader.Activate(info, this);
        }
    }

    // ---- ViewSlot / ClearSlots internals ----

    private static void RemoveTaggedFrom(ItemCollection? items, object? pluginTag)
    {
        if (items == null) return;
        for (int i = items.Count - 1; i >= 0; i--)
        {
            if (Equals(items[i] is FrameworkElement fe ? fe.Tag : null, pluginTag))
                items.RemoveAt(i);
        }
    }

    private void AddToTitleBarMenu(object content)
    {
        if (_menuPlugins == null) return;
        // Idempotent: if the same plugin re-Activates,
        // replace the existing item with the same Tag
        // rather than appending a duplicate. Plugin
        // authors set Tag = this per the IPluginContext
        // contract, so any existing contribution from this
        // plugin is safe to remove before insertion.
        RemoveTaggedFrom(_menuPlugins.Items, GetPluginTag(content));
        _menuPlugins.Items.Add(content);
    }

    private void AddToViewerContextMenu(object content)
    {
        if (_viewerContextMenu == null) return;
        // Idempotent: drop any existing contribution from
        // this plugin before inserting the new one.
        RemoveTaggedFrom(_viewerContextMenu.Items, GetPluginTag(content));
        // Insert before the last Separator so plugin items
        // group together at the bottom of the menu,
        // matching the layout the old RegisterContextMenuItem
        // produced.
        var insertBefore = _viewerContextMenu.Items.OfType<Separator>().LastOrDefault();
        if (insertBefore != null)
            _viewerContextMenu.Items.Insert(_viewerContextMenu.Items.IndexOf(insertBefore), content);
        else
            _viewerContextMenu.Items.Add(content);
    }

    private static object? GetPluginTag(object content)
    {
        // Plugins are expected to set FrameworkElement.Tag
        // to their own IPluginModule instance per the
        // IPluginContext contract. Read it here so
        // AddToTitleBarMenu / AddToViewerContextMenu can
        // dedup by-tag without the plugin having to track
        // its own slot list.
        if (content is FrameworkElement fe) return fe.Tag;
        return null;
    }

    // ---- Menu item rendering + handlers ----

    /// <summary>Build the [dot] [plugin name] header layout
    /// for a plugin toggle. The dot is stored as the
    /// Border's Tag so SubmenuOpened / Checked / Unchecked
    /// can find it and swap the Background brush when the
    /// status changes.</summary>
    private Grid BuildPluginHeader(PluginInfo info)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // 8×8 dot with 4 corner-radius = circle. Same
        // proportions as the LinearDot style in Styles/Tag.xaml.
        var dot = new Border
        {
            Width = 8,
            Height = 8,
            CornerRadius = new CornerRadius(4),
            Background = GetStatusBrush(info.Instance.Status),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
            Tag = "PluginStatusDot",
        };
        Grid.SetColumn(dot, 0);
        grid.Children.Add(dot);

        var text = new TextBlock
        {
            Text = info.Name,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        return grid;
    }

    /// <summary>When the 插件 submenu is first opened, sync
    /// each plugin toggle's IsChecked with the persisted
    /// SettingsStore state and refresh the status dot
    /// (which can change between opens — e.g. the user
    /// copied model files in, or the warmup finished).
    /// WPF ContextMenus don't realize their child MenuItems
    /// until the menu is shown, so this is the first point
    /// where the IsChecked setter actually takes effect.</summary>
    private void MenuPlugins_SubmenuOpened(object sender, RoutedEventArgs e)
    {
        if (_menuPlugins == null) return;
        foreach (var item in _menuPlugins.Items.OfType<MenuItem>())
        {
            if (item.Tag is not PluginInfo info) continue;
            // IsChecked setter suppresses re-entrancy if the
            // value doesn't change, so calling this on every
            // open is safe and cheap.
            item.IsChecked = _settings.IsPluginEnabled(info.Name);
            RefreshPluginDot(item, info);
            item.IsEnabled = info.Instance.Status != PluginStatus.Unavailable;
        }
    }

    private void PluginItem_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem item) return;
        if (item.Tag is not PluginInfo info) return;
        PluginLoader.Activate(info, this);
        _settings.SetPluginEnabled(info.Name, true);
        // Refresh the dot — status may have transitioned to
        // Enabled (green). SetChecked doesn't fire
        // SubmenuOpened.
        RefreshPluginDot(item, info);
    }

    private void PluginItem_Unchecked(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem item) return;
        if (item.Tag is not PluginInfo info) return;
        PluginLoader.Deactivate(info);
        _settings.SetPluginEnabled(info.Name, false);
        // Refresh the dot — status transitioned back to
        // Disabled (gray). SetChecked doesn't fire
        // SubmenuOpened.
        RefreshPluginDot(item, info);
    }

    /// <summary>Find the status dot inside the item's Grid
    /// header and update its Background brush. The dot is
    /// tagged "PluginStatusDot" in BuildPluginHeader so we
    /// can find it without walking every Border
    /// descendant.</summary>
    private void RefreshPluginDot(MenuItem item, PluginInfo info)
    {
        if (item.Header is not Grid grid) return;
        foreach (var child in grid.Children)
        {
            if (child is Border dot && Equals(dot.Tag, "PluginStatusDot"))
            {
                dot.Background = GetStatusBrush(info.Instance.Status);
                return;
            }
        }
    }

    /// <summary>Map <see cref="PluginStatus"/> to the
    /// Linear design-token brush for the status dot. Reuses
    /// the design tokens the rest of the app uses for
    /// status colors so the dot visually matches the main
    /// interface's status indicators (info pill dot, status
    /// pills, etc.).</summary>
    private System.Windows.Media.Brush GetStatusBrush(PluginStatus status) => status switch
    {
        PluginStatus.Enabled => _theme.StatusGreen,
        PluginStatus.Unavailable => _theme.StatusRed,
        _ => _theme.TextTertiary,  // gray for Disabled
    };
}
