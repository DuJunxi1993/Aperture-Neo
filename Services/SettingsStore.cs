using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ApertureNeo.Models;

namespace ApertureNeo.Services;

/// <summary>
/// Persists user-mutable state (favorites, recent folders,
/// last-opened image path) to <c>%APPDATA%\ApertureNeo\settings.json</c>.
/// Reads/writes are guarded by a single lock; saves are debounced
/// (<see cref="ScheduleSave"/>) so a burst of AddRecent/AddFavorite
/// calls collapses into one disk write. <see cref="FavoritesChanged"/>
/// and <see cref="RecentChanged"/> events let the tree refresh
/// without polling.
/// </summary>
public class SettingsStore : ISettingsStore
{
    public const int MaxRecentCount = 10;

    private static readonly string AppDataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ApertureNeo");
    private static readonly string SettingsPath = Path.Combine(AppDataDir, "settings.json");

    private readonly object _lock = new();
    private readonly List<string> _favorites = new();
    private readonly List<RecentEntry> _recent = new();
    // Plugins the user has enabled via the 插件 submenu checkbox.
    // Opt-in: a fresh settings.json (or a settings.json that
    // predates this feature) starts with an empty list, so no
    // plugins are auto-enabled — the user has to opt in once and
    // the choice persists across sessions.
    private readonly HashSet<string> _enabledPlugins = new();
    private CancellationTokenSource? _saveCts;
    private int _saveGeneration;
    // P0 fix: defer file IO until first access. The previous
    // behaviour called Load() in the DI factory, which ran BEFORE
    // App.OnStartup's MigrateLegacyData() had a chance to move the
    // old ImageViewerNeo / HighSpeedImageViewer settings.json into
    // ApertureNeo's path. On first run after the rename, the user
    // would see empty favorites / recent until the next save clobbered
    // the old file (or it got re-loaded manually). The new pattern
    // lets App.OnStartup migrate the file before any read happens.
    private bool _loaded;

    public IReadOnlyList<string> Favorites
    {
        get { Load(); lock (_lock) return _favorites.ToList(); }
    }

    public IReadOnlyList<RecentEntry> Recent
    {
        get { Load(); lock (_lock) return _recent.ToList(); }
    }

    public event Action? FavoritesChanged;
    public event Action? RecentChanged;

    /// <summary>
    /// Path of the last image the user was viewing. Persisted so a
    /// theme switch (which closes+reopens the main window) can
    /// restore the user's position instead of dropping them back at
    /// the first image in the folder.
    /// </summary>
    public string? LastOpenedImage { get; set; }

    /// <summary>
    /// Read <see cref="SettingsPath"/> from disk and replace the
    /// in-memory favorites, recent, and last-opened-image values.
    /// Idempotent and lazy — the first accessor call (Favorites,
    /// Recent, IsFavorite, …) calls this if it hasn't run yet.
    /// Silent on missing file or parse error — starts with empty
    /// state.
    /// </summary>
    public void Load()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            if (!File.Exists(SettingsPath)) return;
            var json = File.ReadAllText(SettingsPath);
            var data = JsonSerializer.Deserialize<SettingsData>(json);
            if (data == null) return;
            lock (_lock)
            {
                _favorites.Clear();
                _favorites.AddRange(data.Favorites ?? new List<string>());
                _recent.Clear();
                _recent.AddRange(data.Recent ?? new List<RecentEntry>());
                _enabledPlugins.Clear();
                foreach (var p in data.EnabledPlugins ?? new List<string>())
                    _enabledPlugins.Add(p);
            }
            LastOpenedImage = data.LastOpenedImage;
        }
        catch
        {
        }
    }

    /// <summary>Force re-load from disk on the next access.
    /// Used after <see cref="MigrateLegacyData"/> moves the old
    /// settings.json into place: the in-memory cache is invalidated
    /// so the next read picks up the migrated file instead of an
    /// empty (or stale) state.</summary>
    public void Reload() => _loaded = false;

    /// <summary>
    /// Serialize the current favorites, recent list, and last-opened
    /// image to <see cref="SettingsPath"/>. Synchronous; called by
    /// <see cref="ScheduleSave"/> after a short debounce, and by
    /// <c>App.OnExit</c> on application close. Silent on IO error
    /// (logged but not propagated).
    /// </summary>
    public void Save()
    {
        try
        {
            List<string> favs;
            List<RecentEntry> recs;
            List<string> enabled;
            string? lastImage;
            lock (_lock)
            {
                favs = _favorites.ToList();
                recs = _recent.ToList();
                enabled = _enabledPlugins.ToList();
                lastImage = LastOpenedImage;
            }
            Directory.CreateDirectory(AppDataDir);
            var json = JsonSerializer.Serialize(
                new SettingsData
                {
                    Favorites = favs,
                    Recent = recs,
                    EnabledPlugins = enabled,
                    LastOpenedImage = lastImage
                },
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
        }
    }

    /// <summary>True if <paramref name="path"/> is in the favorites list
    /// (case-insensitive). Safe to call from any thread.</summary>
    public bool IsFavorite(string path)
    {
        Load();
        lock (_lock)
            return _favorites.Any(p => p.Equals(path, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>True if <paramref name="pluginName"/> is in the
    /// enabled-plugins list. Plugins are opt-in: the user has to
    /// check the box in the 插件 submenu to enable them, and the
    /// choice persists across sessions.</summary>
    public bool IsPluginEnabled(string pluginName)
    {
        Load();
        lock (_lock)
            return _enabledPlugins.Any(p => p == pluginName);
    }

    /// <summary>Add or remove <paramref name="pluginName"/> from the
    /// enabled-plugins list. Triggers a debounced save.</summary>
    public void SetPluginEnabled(string pluginName, bool enabled)
    {
        bool changed;
        lock (_lock)
        {
            changed = enabled
                ? _enabledPlugins.Add(pluginName)
                : _enabledPlugins.Remove(pluginName);
        }
        if (changed) ScheduleSave();
    }

    /// <summary>Add <paramref name="path"/> to favorites if not
    /// already present. Triggers a debounced save and fires
    /// <see cref="FavoritesChanged"/> when the list actually changed.</summary>
    public void AddFavorite(string path)
    {
        bool changed;
        lock (_lock)
        {
            changed = !_favorites.Any(p => p.Equals(path, StringComparison.OrdinalIgnoreCase));
            if (changed) _favorites.Add(path);
        }
        if (changed)
        {
            ScheduleSave();
            FavoritesChanged?.Invoke();
        }
    }

    /// <summary>Remove <paramref name="path"/> from favorites
    /// (case-insensitive). No-op if absent. Triggers save + event
    /// only on actual change.</summary>
    public void RemoveFavorite(string path)
    {
        bool changed;
        lock (_lock)
        {
            changed = _favorites.RemoveAll(p => p.Equals(path, StringComparison.OrdinalIgnoreCase)) > 0;
        }
        if (changed)
        {
            ScheduleSave();
            FavoritesChanged?.Invoke();
        }
    }

    /// <summary>Remove <paramref name="path"/> from the recent
    /// list. No-op if absent.</summary>
    public void RemoveRecent(string path)
    {
        bool changed;
        lock (_lock) { changed = _recent.RemoveAll(p => p.Path.Equals(path, StringComparison.OrdinalIgnoreCase)) > 0; }
        if (changed) { ScheduleSave(); RecentChanged?.Invoke(); }
    }

    /// <summary>Drop the entire recent list. Fires
    /// <see cref="RecentChanged"/> only if the list was non-empty.</summary>
    public void ClearRecent()
    {
        bool changed;
        lock (_lock) { changed = _recent.Count > 0; _recent.Clear(); }
        if (changed) { ScheduleSave(); RecentChanged?.Invoke(); }
    }

    /// <summary>Move <paramref name="path"/> to the front of the
    /// recent list and update its <see cref="RecentEntry.LastOpened"/>
    /// to UtcNow. Trims the list to <see cref="MaxRecentCount"/> by
    /// dropping the oldest entries.</summary>
    public void AddRecent(string path)
    {
        bool changed;
        lock (_lock)
        {
            _recent.RemoveAll(p => p.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
            _recent.Insert(0, new RecentEntry { Path = path, LastOpened = DateTime.UtcNow });
            if (_recent.Count > MaxRecentCount)
            {
                changed = true;
                var trimmed = _recent.Take(MaxRecentCount).ToList();
                _recent.Clear();
                _recent.AddRange(trimmed);
            }
            else
            {
                changed = true;
            }
        }
        if (changed)
        {
            ScheduleSave();
            RecentChanged?.Invoke();
        }
    }

    private void ScheduleSave()
    {
        _saveCts?.Cancel();
        _saveCts?.Dispose();
        _saveCts = new CancellationTokenSource();
        var ct = _saveCts.Token;
        int myGen = Interlocked.Increment(ref _saveGeneration);
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(500, ct);
                if (ct.IsCancellationRequested) return;
                if (myGen != Volatile.Read(ref _saveGeneration)) return;
                Save();
            }
            catch (TaskCanceledException) { }
        }, ct);
    }

    private class SettingsData
    {
        public List<string>? Favorites { get; set; }
        public List<RecentEntry>? Recent { get; set; }

        /// <summary>
        /// Plugin names the user has enabled via the 插件 submenu
        /// checkbox. Empty by default — plugins are opt-in.
        /// </summary>
        public List<string>? EnabledPlugins { get; set; }

        /// <summary>
        /// Path of the last image the user was viewing, persisted so a
        /// theme switch (which closes+reopens the main window) can
        /// restore the user's position instead of dropping them back at
        /// the first image in the folder. Null when the app closed
        /// without an open image.
        /// </summary>
        public string? LastOpenedImage { get; set; }
    }
}
