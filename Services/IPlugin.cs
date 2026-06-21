using System;
using System.Collections.Generic;

namespace ApertureNeo.Services;

/// <summary>
/// Plugin lifecycle status. The 插件 submenu shows a small colored
/// dot to the left of each plugin name based on this value so the
/// user can tell at a glance whether a plugin is on / off /
/// unusable. <see cref="IPlugin.Status"/> is read at menu-open time
/// so it always reflects the current state (e.g. if a dependency
/// goes missing, the next menu open shows red).
/// </summary>
public enum PluginStatus
{
    /// <summary>
    /// Plugin was discovered successfully and can be enabled. Not
    /// currently active. Shows a gray dot.
    /// </summary>
    Disabled,

    /// <summary>
    /// Plugin is currently enabled and its heavy resources are
    /// loaded. Shows a green dot.
    /// </summary>
    Enabled,

    /// <summary>
    /// Plugin can't run (e.g. its model files or native dependencies
    /// are missing). The toggle in the 插件 submenu is disabled so
    /// the user can't enable it. Shows a red dot.
    /// </summary>
    Unavailable,
}

/// <summary>
/// Plugin contract. Plugins are discovered at startup (DLLs are loaded
/// so Name/Description/Status can be read) but NOT activated — the user
/// opts in via a checkbox in the 插件 submenu. Activation triggers the
/// plugin's heavy work (loading native deps, ONNX models, etc.) and
/// registers its menu items. Deactivation reverses both.
/// </summary>
public interface IPlugin
{
    string Name { get; }

    string Description { get; }

    /// <summary>
    /// Current lifecycle status. The 插件 submenu reads this on every
    /// open (via SubmenuOpened) to sync the colored status dot, so
    /// the property should return the live state — not a cached
    /// snapshot. Cheap to call; no IO expected on the read path.
    /// </summary>
    PluginStatus Status { get; }

    /// <summary>
    /// Called when the user enables the plugin. The plugin should
    /// initialize any heavy resources (ONNX runtime, native libs) and
    /// register its menu items via the context. Called once per
    /// enable cycle; pair every Activate with a Deactivate.
    /// </summary>
    void Activate(IPluginContext context);

    /// <summary>
    /// Called when the user disables the plugin. The plugin should
    /// release any heavy resources and unregister its menu items via
    /// <see cref="IPluginContext.UnregisterPluginMenuItems"/>. After
    /// Deactivate returns, the plugin is dormant — it can be
    /// re-activated later by another Activate call.
    /// </summary>
    void Deactivate();
}

public interface IPluginContext
{
    string? CurrentImagePath { get; }

    IReadOnlyList<string> SelectedImagePaths { get; }

    event EventHandler<string?>? CurrentImageChanged;

    /// <summary>
    /// Add a top-level menu item to the 插件 submenu in the overflow
    /// menu. The item's Tag should be set to the IPlugin instance so
    /// UnregisterPluginMenuItems can find and remove it later.
    /// </summary>
    void RegisterMenuItem(System.Windows.Controls.MenuItem item);

    /// <summary>
    /// Add a context-menu item to the image viewer's right-click menu.
    /// The item's Tag should be set to the IPlugin instance.
    /// </summary>
    void RegisterContextMenuItem(System.Windows.Controls.MenuItem item);

    /// <summary>
    /// Remove all menu items (overflow submenu + image context menu)
    /// whose Tag equals <paramref name="pluginTag"/>. Called by the
    /// plugin from Deactivate so it can clean up after itself.
    /// </summary>
    void UnregisterPluginMenuItems(object pluginTag);
}

public sealed record PluginLoadResult(IPlugin? Plugin, string AssemblyPath, Exception? LoadError)
{
    public bool Success => LoadError == null && Plugin != null;
}

/// <summary>
/// Metadata for a discovered plugin. The instance is already
/// constructed (so Name/Description can be read for the menu) but
/// Activate has NOT been called yet — the plugin's heavy resources
/// are not loaded. Call <see cref="PluginLoader.Activate"/> to enable.
/// </summary>
public sealed record PluginInfo(
    string Name,
    string Description,
    IPlugin Instance,
    string AssemblyPath);
