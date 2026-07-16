using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ApertureNeo.Models;

namespace ApertureNeo.Services;

/// <summary>
/// Persists user-mutable state (favorites, recent folders, last-opened
/// image, enabled plugins) to <c>%APPDATA%\ApertureNeo\settings.json</c>.
/// The concrete <see cref="SettingsStore"/> implementation is built
/// by <see cref="AppHost"/> and registered as a singleton in the DI
/// container. Consumers should depend on this interface so they can
/// be unit-tested with a mock.
/// </summary>
public interface ISettingsStore
{
    /// <summary>Sorted list of favorite folder paths.</summary>
    IReadOnlyList<string> Favorites { get; }

    /// <summary>Recent folder paths (newest first, capped at <see cref="MaxRecentCount"/>).</summary>
    IReadOnlyList<RecentEntry> Recent { get; }

    /// <summary>
    /// Path of the last image the user was viewing. Persisted so a
    /// theme switch (which closes + reopens the main window) can
    /// restore the user's position instead of dropping them back at
    /// the first image in the folder.
    /// </summary>
    string? LastOpenedImage { get; set; }

    /// <summary>
    /// User-chosen default directory for the screenshot editor's
    /// Save button. Read on editor open (initial folder), written
    /// when the user checks "set as default" in the save dialog.
    /// Null = no preference; the editor falls back to
    /// <c>%USERPROFILE%\Pictures\ApertureNeo\Screenshots</c>.
    /// Shared between the main app and the standalone ScreenshotTool
    /// via the same <c>settings.json</c> file.
    /// </summary>
    string? DefaultScreenshotSaveDirectory { get; set; }

    /// <summary>
    /// P5: when true, the screenshot editor's OCR button opens
    /// <c>OcrResultWindow</c> in addition to copying to clipboard
    /// and showing a toast. When false (default), only clipboard
    /// + toast (the "fast" path — the user can always re-OCR via
    /// the OCR plugin menu if they want the full window).
    /// Controlled by the "OCR 显示结果窗口" CheckBox in the
    /// 插件 submenu. Persisted across sessions.
    /// </summary>
    bool EditorOcrShowWindow { get; set; }

    /// <summary>
    /// P5 (tray-resident lifecycle): when true (the default on
    /// a fresh install), the main window's title-bar close
    /// button only hides the window to the tray; the process
    /// keeps running so global hotkeys continue to work. Set
    /// false via the settings panel to make the close button
    /// exit the process immediately.
    /// </summary>
    bool CloseToTray { get; set; }

    /// <summary>
    /// Whether the app should auto-start with Windows. Mirrored
    /// into HKCU\...\Run\ApertureNeo by the settings panel
    /// (the registry write is decoupled from this property so
    /// a future portable install can implement its own
    /// persistence).
    /// </summary>
    bool AutoStart { get; set; }

    /// <summary>
    /// Set true once the first-run onboarding dialog has
    /// been shown. The dialog is shown only when this is
    /// false; subsequent launches skip it.
    /// </summary>
    bool FirstRunShown { get; set; }

    /// <summary>
    /// Snapshot of the current shortcut bindings. Falls back
    /// to the defaults baked into <see cref="SettingsStore"/>
    /// on a fresh install, so the user always sees a populated
    /// table.
    /// </summary>
    IReadOnlyDictionary<string, string> GetShortcuts();

    /// <summary>
    /// Replace or remove a single shortcut binding. Pass
    /// <c>null</c> to remove the override and fall back to
    /// the default.
    /// </summary>
    void SetShortcut(string id, string? gesture);

    /// <summary>
    /// Drop every user-customised shortcut binding so the
    /// next read returns the canonical defaults. Used by
    /// the settings panel's "重置全部" button.
    /// </summary>
    void ResetShortcuts();

    /// <summary>Raised after a successful Add/RemoveFavorite.</summary>
    event Action? FavoritesChanged;

    /// <summary>Raised after a successful Add/RemoveRecent or ClearRecent.</summary>
    event Action? RecentChanged;

    /// <summary>Read settings.json from disk. Silent on missing file / parse errors.</summary>
    void Load();

    /// <summary>Serialize current state to settings.json. Silent on IO errors.</summary>
    void Save();

    /// <summary>True if <paramref name="path"/> is in the favorites list (case-insensitive).</summary>
    bool IsFavorite(string path);

    /// <summary>True if <paramref name="pluginName"/> is in the enabled-plugins list.</summary>
    bool IsPluginEnabled(string pluginName);

    /// <summary>Add/remove <paramref name="pluginName"/> from enabled-plugins. Triggers a debounced save.</summary>
    void SetPluginEnabled(string pluginName, bool enabled);

    /// <summary>True if the user has ever toggled a plugin (the
    /// enabled-plugins list has been touched). Used by the
    /// app's first-run bootstrap to decide whether to opt the
    /// user into the default "all plugins enabled" state or
    /// honour their persisted choices. The list can still be
    /// empty (e.g. the user explicitly disabled everything) —
    /// we only need to know it was non-empty at some point.
    /// </summary>
    bool HasAnyExplicitPluginChoice();

    /// <summary>Add <paramref name="path"/> to favorites if not present. Fires FavoritesChanged on actual change.</summary>
    void AddFavorite(string path);

    /// <summary>Remove <paramref name="path"/> from favorites (case-insensitive). No-op if absent.</summary>
    void RemoveFavorite(string path);

    /// <summary>Remove <paramref name="path"/> from recent. No-op if absent.</summary>
    void RemoveRecent(string path);

    /// <summary>Drop the entire recent list. Fires RecentChanged only if non-empty.</summary>
    void ClearRecent();

    /// <summary>
    /// Move <paramref name="path"/> to the front of recent and update
    /// its LastOpened to UtcNow. Trims to MaxRecentCount.
    /// </summary>
    void AddRecent(string path);
}