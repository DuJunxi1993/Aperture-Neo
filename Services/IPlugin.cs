using System;
using System.Collections.Generic;

namespace ApertureNeo.Services;

/// <summary>
/// Plugin lifecycle status. The 插件 submenu shows a small colored
/// dot to the left of each plugin name based on this value so the
/// user can tell at a glance whether a plugin is on / off /
/// unusable. <see cref="IPluginModule.Status"/> is read at menu-open time
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
/// registers its UI via <see cref="IPluginContext.ViewSlot"/>. Deactivation
/// reverses both.
///
/// P4 rename: was <c>IPlugin</c>. The "Module" suffix reflects the
/// ViewSlot / ShellRegions upgrade — a module contributes UI to
/// named shell regions rather than poking at a specific MenuItem
/// collection. Future plugins (e.g. an "Image Resize" plugin adding
/// thumbnail context-menu items, a "Color Profile" plugin adding
/// its own status-bar widget) use the same ViewSlot pattern.
/// </summary>
public interface IPluginModule
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
    /// contribute its UI to the shell via
    /// <see cref="IPluginContext.ViewSlot(string, object)"/>. Called
    /// once per enable cycle; pair every Activate with a Deactivate.
    /// </summary>
    void Activate(IPluginContext context);

    /// <summary>
    /// Called when the user disables the plugin. The plugin should
    /// release any heavy resources and call
    /// <see cref="IPluginContext.ClearSlots(object)"/> with itself as
    /// the tag to remove all UI it contributed. After Deactivate
    /// returns, the plugin is dormant — it can be re-activated later
    /// by another Activate call.
    /// </summary>
    void Deactivate();
}

/// <summary>
/// Shell-provided API for plugins. Plugins use the context to read
/// app state (current image, selection) and to contribute UI to
/// named shell regions (<see cref="ShellRegions"/>) via
/// <see cref="ViewSlot"/>.
///
/// P4: the per-region registration methods (RegisterMenuItem,
/// RegisterContextMenuItem, UnregisterPluginMenuItems) that
/// IPluginContext used to expose are gone. Plugins use the
/// region-based <see cref="ViewSlot"/> / <see cref="ClearSlots"/>
/// pattern instead — cleaner, more general, and ready for
/// non-menu regions (status bar, toolbar, thumbnail context
/// menu, etc.).
/// </summary>
public interface IPluginContext
{
    /// <summary>
    /// Path of the currently-displayed image, or null if the
    /// folder is empty / viewer is showing the empty state.
    /// </summary>
    string? CurrentImagePath { get; }

    /// <summary>
    /// All currently-selected image paths. For the single-selection
    /// viewer this is always either <c>{currentImagePath}</c> or an
    /// empty list. Multi-selection support is a future extension.
    /// </summary>
    IReadOnlyList<string> SelectedImagePaths { get; }

    /// <summary>
    /// Raised when the current image changes (navigation, folder
    /// load). Plugins subscribe to drive background work — e.g.
    /// the OCR plugin pre-decodes the image as soon as it changes
    /// so the "提取当前图片文字" click feels instant.
    /// </summary>
    event EventHandler<string?>? CurrentImageChanged;

    /// <summary>
    /// Contribute <paramref name="content"/> to the shell region
    /// named <paramref name="region"/>. The shell routes the
    /// content to the right visual-tree location based on the
    /// region constant in <see cref="ShellRegions"/>.
    ///
    /// Typical content: a MenuItem (added to a menu), a UIElement
    /// (added to a toolbar / overlay), a StatusBar widget, etc.
    /// Plugins must set <see cref="System.Windows.FrameworkElement.Tag"/>
    /// on the content to <c>this</c> so <see cref="ClearSlots"/>
    /// can find and remove it later.
    ///
    /// Calling ViewSlot twice with the same region + same plugin
    /// instance is idempotent — the shell replaces the previous
    /// content rather than accumulating duplicates (this lets a
    /// plugin refresh its UI without leaking items on Activate).
    /// </summary>
    void ViewSlot(string region, object content);

    /// <summary>
    /// Remove every piece of UI that was contributed via
    /// <see cref="ViewSlot"/> whose <c>Tag</c> equals
    /// <paramref name="pluginTag"/>. Called by the plugin from
    /// Deactivate so it can clean up after itself.
    /// </summary>
    void ClearSlots(object pluginTag);

    /// <summary>
    /// Resolve a registered DI service (e.g. <see cref="ISettingsStore"/>,
    /// <see cref="IUiState"/>) from the host's service container.
    /// Returns null if the service is not registered. Plugins use
    /// this to read app-wide settings (e.g. the screenshot plugin
    /// reads the user's "OCR 显示结果窗口" preference here) without
    /// having to take a hard dependency on the host's container
    /// type.
    /// </summary>
    T? GetService<T>() where T : class;
}

/// <summary>
/// Named shell regions that plugins can contribute UI to via
/// <see cref="IPluginContext.ViewSlot"/>. Keep this list small
/// and well-defined — every region is a public commitment to
/// plugin authors about where their UI will land.
///
/// P4: only two regions are wired up today. Future regions
/// (thumbnail context menu, status bar, custom overlay,
/// keyboard shortcut bindings) extend this list.
/// </summary>
public static class ShellRegions
{
    /// <summary>
    /// Top-level items inside the 插件 submenu in the title bar's
    /// overflow menu. The 插件 submenu itself is the on/off toggle
    /// host — this region is for plugins that want to add OTHER
    /// items alongside their toggle (rare; most plugins only use
    /// the ViewerContextMenu region).
    /// </summary>
    public const string TitleBarMenu = "TitleBar.Menu";

    /// <summary>
    /// Items inside the image viewer's right-click context menu.
    /// The most common plugin region — most plugins add one
    /// MenuItem here that runs an action on the current image.
    /// </summary>
    public const string ViewerContextMenu = "Viewer.ContextMenu";
}

public sealed record PluginLoadResult(IPluginModule? Plugin, string AssemblyPath, Exception? LoadError)
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
    IPluginModule Instance,
    string AssemblyPath);