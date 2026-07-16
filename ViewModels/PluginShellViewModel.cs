using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
/// SkiaImageViewer's right-click context menu (where plugins
/// add items via <see cref="ViewSlot(string, object)"/>); the
/// title-bar 插件 submenu has been retired (Stage 4 — toggles
/// live in the Settings → Plugins panel now) so this class no
/// longer renders anything title-bar related.
///
/// Plugins still get the same <see cref="IPluginContext"/>
/// contract they always had:
/// <list type="bullet">
///   <item><see cref="CurrentImagePath"/>, <see cref="SelectedImagePaths"/> — read app state.</item>
///   <item><see cref="CurrentImageChanged"/> event — subscribe for navigation-driven work.</item>
///   <item><see cref="ViewSlot(string, object)"/> — contribute UI to <see cref="ShellRegions.ViewerContextMenu"/>.</item>
///   <item><see cref="ClearSlots(object)"/> — clean up at Deactivate.</item>
///   <item><see cref="GetService{T}()"/> — DI lookup.</item>
/// </list>
///
/// Activation is now driven by the Settings → Plugins panel:
/// <see cref="SetPluginEnabled(PluginInfo, bool)"/> calls
/// <see cref="PluginLoader.Activate(PluginInfo, IPluginContext)"/> /
/// <see cref="PluginLoader.Deactivate(PluginInfo)"/> and persists
/// the choice. App startup calls <see cref="RestoreEnabledPlugins"/>
/// once after discovery to re-enable the user's persisted set.
/// </summary>
public partial class PluginShellViewModel : ObservableObject, IPluginContext
{
    private readonly ISettingsStore _settings;
    private readonly INavigationService _navigation;

    /// <summary>The SkiaImageViewer's right-click context
    /// menu (declared in ImageViewerPanelView.xaml). Set by
    /// <see cref="AttachShell"/>. Plugins' <c>ViewSlot(
    /// ShellRegions.ViewerContextMenu, item)</c> add their
    /// menu items here.</summary>
    private ContextMenu? _viewerContextMenu;

    /// <summary>Discovered plugins, set by <see cref="SetAvailablePlugins"/>.
    /// Held so <see cref="RestoreEnabledPlugins"/> can iterate and
    /// re-activate the ones the user previously enabled.
    ///
    /// Stored as an <see cref="ObservableCollection{T}"/> so the
    /// Settings → Plugins panel can subscribe to
    /// <see cref="ObservableCollection{T}.CollectionChanged"/> and
    /// rebuild its row templates when the discovery list changes
    /// (e.g. a hot-reload of plugins without an app restart).
    /// The public <see cref="AvailablePlugins"/> getter returns
    /// this collection as <see cref="IReadOnlyList{T}"/> for
    /// read-only access by callers that don't need to mutate.</summary>
    private readonly ObservableCollection<PluginInfo> _availablePlugins = new();

    public PluginShellViewModel(ISettingsStore settings, INavigationService navigation)
    {
        _settings = settings;
        _navigation = navigation;
        // Re-raise navigation events as IPluginContext events
        // so plugins (notably OCR's "pre-decode the current
        // image" warmup) observe the same navigation the
        // ViewModels do.
        _navigation.CurrentImageChanged += item => CurrentImageChanged?.Invoke(this, item?.FilePath);
    }

    /// <summary>Read-only view of plugins discovered at startup.
    /// The Settings → Plugins panel reads this to render its
    /// per-plugin toggle rows. The collection is mutable
    /// internally (see <see cref="SetAvailablePlugins"/>) but
    /// callers should not modify it directly — subscribe to
    /// <see cref="ObservableCollection{T}.CollectionChanged"/>
    /// to react to changes instead.</summary>
    public IReadOnlyList<PluginInfo> AvailablePlugins => _availablePlugins;

    /// <summary>Connect the viewer ContextMenu (plugins'
    /// ViewerContextMenu slot). Call once from MainWindow's
    /// ctor after InitializeComponent has resolved the XAML
    /// reference. The title-bar MenuItem is gone — plugin
    /// toggles live in the Settings panel now.</summary>
    public void AttachShell(ContextMenu viewerContextMenu)
    {
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
        if (_viewerContextMenu != null)
            RemoveTaggedFrom(_viewerContextMenu.Items, pluginTag);
    }

    public T? GetService<T>() where T : class
    {
        // Forward to the AppHost's DI container. We don't take
        // a hard dependency on the container type so plugins
        // can be unit-tested with a mock context.
        return AppHost.Services?.GetService(typeof(T)) as T;
    }

    // ---- Discovery / activation ----

    /// <summary>Called by App.RunViewerAsync after
    /// <c>PluginLoader.Discover</c>. Caches the plugin list
    /// for later activation; the Settings → Plugins panel
    /// reads <see cref="AvailablePlugins"/> to render
    /// toggles.
    ///
    /// Clear+Add (not replace-the-reference) so the
    /// underlying <see cref="ObservableCollection{T}"/>
    /// fires <c>CollectionChanged</c> — SettingsViewModel
    /// subscribes to that to know when to re-render the
    /// plugin row templates. Replacing the reference would
    /// orphan the old collection's events.</summary>
    public void SetAvailablePlugins(IReadOnlyList<PluginInfo> plugins)
    {
        _availablePlugins.Clear();
        foreach (var p in plugins) _availablePlugins.Add(p);
    }

    /// <summary>Read the persisted enabled-plugins list
    /// from <see cref="ISettingsStore"/> and activate the
    /// previously-enabled plugins. Called once at startup,
    /// after discovery.</summary>
    public void RestoreEnabledPlugins()
    {
        foreach (var info in _availablePlugins)
        {
            if (!_settings.IsPluginEnabled(info.Name)) continue;
            PluginLoader.Activate(info, this);
        }
    }

    /// <summary>Toggle a single plugin on/off. Called from
    /// the Settings → Plugins panel's checkbox. Activates or
    /// deactivates via <see cref="PluginLoader"/> and persists
    /// the choice to <see cref="ISettingsStore"/>.</summary>
    public void SetPluginEnabled(PluginInfo info, bool enabled)
    {
        if (enabled)
            PluginLoader.Activate(info, this);
        else
            PluginLoader.Deactivate(info);
        _settings.SetPluginEnabled(info.Name, enabled);
    }

    // ---- ViewSlot internals ----

    private static void RemoveTaggedFrom(ItemCollection? items, object? pluginTag)
    {
        if (items == null) return;
        for (int i = items.Count - 1; i >= 0; i--)
        {
            if (Equals(items[i] is FrameworkElement fe ? fe.Tag : null, pluginTag))
                items.RemoveAt(i);
        }
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
            _viewerContextMenu.Items.Insert(_viewerContextMenu.Items.IndexOf(insertBefore) + 1, content);
        else
            _viewerContextMenu.Items.Add(content);
    }

    private static object? GetPluginTag(object content)
    {
        // Plugins are expected to set FrameworkElement.Tag
        // to their own IPluginModule instance per the
        // IPluginContext contract. Read it here so
        // AddToViewerContextMenu can dedup by-tag without
        // the plugin having to track its own slot list.
        if (content is FrameworkElement fe) return fe.Tag;
        return null;
    }
}