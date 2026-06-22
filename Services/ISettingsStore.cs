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