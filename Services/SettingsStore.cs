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
/// last-opened image path, default screenshot save directory,
/// enabled plugins) to <c>%APPDATA%\ApertureNeo\settings.json</c>.
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
    // Set the first time SetPluginEnabled is called (during a
    // run, not on first read). Lets the app's startup code
    // distinguish "fresh user — opt them into defaults" from
    // "advanced user — respect their empty list". Persisted
    // across runs via the SettingsData class below.
    private bool _pluginChoicesTouched;
    private string? _defaultSaveDir;
    // P5 (tray-resident lifecycle): the user-configurable
    // keyboard-shortcut bindings. Keyed by stable id
    // ("ScreenshotPlugin.CaptureArea"), values are WPF
    // KeyGesture strings ("Ctrl+Alt+A"). Default values
    // are baked into GetShortcuts() so the user always
    // has working bindings even on a brand-new install.
    private Dictionary<string, string> _shortcuts = new();
    private bool _autoStart;
    private bool _firstRunShown;
    private bool _closeToTray = true;
    private CancellationTokenSource? _saveCts;
    private int _saveGeneration;
    private readonly object _saveLock = new();
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
    /// User-chosen default directory for the screenshot editor's
    /// Save button. Read on editor open (initial folder), written
    /// when the user checks "set as default" in the save dialog.
    /// Shared between the main app and the standalone ScreenshotTool
    /// because both read/write the same settings.json.
    /// </summary>
    public string? DefaultScreenshotSaveDirectory
    {
        get { Load(); lock (_lock) return _defaultSaveDir; }
        set
        {
            bool changed;
            lock (_lock)
            {
                changed = _defaultSaveDir != value;
                _defaultSaveDir = value;
            }
            if (changed) ScheduleSave();
        }
    }

    /// <summary>
    /// P5: gates whether the editor's OCR button opens the full
    /// result window. Persisted in settings.json. Default false
    /// (clipboard-only) — the user opts in via the "OCR 显示结果
    /// 窗口" CheckBox in the 插件 submenu.
    /// </summary>
    public bool EditorOcrShowWindow
    {
        get { Load(); lock (_lock) return _editorOcrShowWindow; }
        set
        {
            bool changed;
            lock (_lock)
            {
                changed = _editorOcrShowWindow != value;
                _editorOcrShowWindow = value;
            }
            if (changed) ScheduleSave();
        }
    }
    private bool _editorOcrShowWindow;

    /// <summary>
    /// Default keyboard-shortcut bindings. Read on every
    /// shortcut lookup; the user can override any of them
    /// in the settings panel. New shortcuts added in
    /// future versions should appear here too so the
    /// "reset to default" path in the UI has a source
    /// of truth.
    /// </summary>
    public static IReadOnlyDictionary<string, string> DefaultShortcuts { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["ScreenshotPlugin.CaptureArea"] = "Ctrl+Alt+A",
        ["ScreenshotPlugin.CaptureOcr"] = "Ctrl+Alt+T",
        // Fullscreen capture: PrintScreen is a near-universal
        // "screenshot" key and rarely conflicts. The
        // standalone ScreenshotTool also defaults to
        // PrintScreen for the fullscreen flow.
        ["ScreenshotPlugin.CaptureFullscreen"] = "PrintScreen",
    };

    /// <summary>
    /// Snapshot of the current shortcut bindings. Returns a
    /// new dictionary on each call (the caller is allowed to
    /// mutate it without affecting the store). Falls back to
    /// <see cref="DefaultShortcuts"/> on a fresh install or
    /// after settings.json has been wiped, so the user
    /// always sees a populated shortcut table.
    /// </summary>
    public IReadOnlyDictionary<string, string> GetShortcuts()
    {
        Load();
        lock (_lock)
        {
            // Merge defaults with overrides so adding a new
            // default shortcut in code doesn't require the
            // user to reset — they get the new default on
            // first read after upgrade.
            var merged = new Dictionary<string, string>(DefaultShortcuts, StringComparer.OrdinalIgnoreCase);
            foreach (var kv in _shortcuts)
                merged[kv.Key] = kv.Value;
            return merged;
        }
    }

    /// <summary>
    /// Replace or remove a single shortcut binding. Pass
    /// <c>null</c> for <paramref name="gesture"/> to remove
    /// the user's override and fall back to the default.
    /// </summary>
    public void SetShortcut(string id, string? gesture)
    {
        bool changed;
        lock (_lock)
        {
            if (gesture == null)
            {
                changed = _shortcuts.Remove(id);
            }
            else
            {
                _shortcuts.TryGetValue(id, out var prev);
                changed = prev != gesture;
                _shortcuts[id] = gesture;
            }
        }
        if (changed) ScheduleSave();
    }

    /// <summary>
    /// Reset all shortcut overrides to defaults. Called from
    /// the settings panel's "重置全部" button.
    /// </summary>
    public void ResetShortcuts()
    {
        lock (_lock)
        {
            if (_shortcuts.Count == 0) return;
            _shortcuts.Clear();
        }
        ScheduleSave();
    }

    /// <summary>
    /// Whether the app should auto-start with Windows.
    /// Written via the settings panel toggle; mirrored into
    /// HKCU\...\Run\ApertureNeo by <see cref="AutoStartService"/>
    /// (the registry write is decoupled from the settings
    /// store so the same toggle works for a future portable
    /// install where registry isn't available).
    /// </summary>
    public bool AutoStart
    {
        get { Load(); lock (_lock) return _autoStart; }
        set
        {
            bool changed;
            lock (_lock)
            {
                changed = _autoStart != value;
                _autoStart = value;
            }
            if (changed) ScheduleSave();
        }
    }

    /// <summary>
    /// Set true once the first-run onboarding dialog has
    /// been shown at least once. The dialog is shown only
    /// when this is false, so subsequent launches skip it.
    /// </summary>
    public bool FirstRunShown
    {
        get { Load(); lock (_lock) return _firstRunShown; }
        set
        {
            bool changed;
            lock (_lock)
            {
                changed = _firstRunShown != value;
                _firstRunShown = value;
            }
            if (changed) ScheduleSave();
        }
    }

    /// <summary>
    /// When true (the default), closing the main window via
    /// its title-bar X button only hides the window to the
    /// tray; the app keeps running so global hotkeys
    /// continue to work. Set false in settings to make the
    /// close button exit the process immediately.
    /// </summary>
    public bool CloseToTray
    {
        get { Load(); lock (_lock) return _closeToTray; }
        set
        {
            bool changed;
            lock (_lock)
            {
                changed = _closeToTray != value;
                _closeToTray = value;
            }
            if (changed) ScheduleSave();
        }
    }

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
        lock (_lock)
        {
            if (_loaded) return;
            _loaded = true;
        }
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
                _defaultSaveDir = data.DefaultScreenshotSaveDirectory;
                _editorOcrShowWindow = data.EditorOcrShowWindow;
                _shortcuts = data.Shortcuts != null
                    ? new Dictionary<string, string>(data.Shortcuts, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                _autoStart = data.AutoStart;
                _firstRunShown = data.FirstRunShown;
                _closeToTray = data.CloseToTray;
                // Restore the "user has touched the plugin
                // list" flag from disk. Defaults to false on
                // a brand-new install or a settings.json that
                // predates this feature, in which case the
                // first-run bootstrap (App.xaml.cs) will opt
                // the user into all discovered plugins.
                _pluginChoicesTouched = data.PluginChoicesTouched;
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
            string? defaultSaveDir;
            bool editorOcrShowWindow;
            Dictionary<string, string> shortcuts;
            bool autoStart;
            bool firstRunShown;
            bool closeToTray;
            bool pluginChoicesTouched;
            lock (_lock)
            {
                favs = _favorites.ToList();
                recs = _recent.ToList();
                enabled = _enabledPlugins.ToList();
                lastImage = LastOpenedImage;
                defaultSaveDir = _defaultSaveDir;
                editorOcrShowWindow = _editorOcrShowWindow;
                shortcuts = new Dictionary<string, string>(_shortcuts);
                autoStart = _autoStart;
                firstRunShown = _firstRunShown;
                closeToTray = _closeToTray;
                pluginChoicesTouched = _pluginChoicesTouched;
            }
            string json;
            lock (_saveLock)
            {
                var dir = Path.GetDirectoryName(SettingsPath);
                if (!string.IsNullOrEmpty(dir))
                    System.IO.Directory.CreateDirectory(dir);
                json = JsonSerializer.Serialize(
                new SettingsData
                {
                    Favorites = favs,
                    Recent = recs,
                    EnabledPlugins = enabled,
                    LastOpenedImage = lastImage,
                    DefaultScreenshotSaveDirectory = defaultSaveDir,
                    EditorOcrShowWindow = editorOcrShowWindow,
                    Shortcuts = shortcuts.Count > 0 ? shortcuts : null,
                    AutoStart = autoStart,
                    FirstRunShown = firstRunShown,
                    CloseToTray = closeToTray,
                    PluginChoicesTouched = pluginChoicesTouched,
                },
                new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsPath, json);
            }
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
            // Mark "user has made a choice" — the first-run
            // bootstrap reads this via HasAnyExplicitPluginChoice
            // to decide whether to opt the user in. Setting
            // the flag regardless of `changed` is fine: even a
            // duplicate "enable already-enabled" call still
            // signals user intent.
            _pluginChoicesTouched = true;
        }
        if (changed) ScheduleSave();
    }

    /// <summary>True if the user has ever interacted with the
    /// enabled-plugins list — i.e. <see cref="SetPluginEnabled"/>
    /// has been called at least once. Even a "disable the only
    /// plugin" call counts: an explicit empty list is
    /// respected, only the unset default is overwritten.
    /// We track this separately from the list length so the
    /// first-run bootstrap can tell "fresh user" from
    /// "advanced user who disabled everything".</summary>
    public bool HasAnyExplicitPluginChoice()
    {
        Load();
        lock (_lock) return _pluginChoicesTouched;
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

    /// <summary>
    /// Move <paramref name="path"/> to the front of the recent list and update
    /// its <see cref="RecentEntry.LastOpened"/>
    /// to UtcNow. Trims the list to <see cref="MaxRecentCount"/> by
    /// dropping the oldest entries.
    /// </summary>
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

        /// <summary>
        /// User-chosen default save directory for the screenshot
        /// editor. Null on first run; both the main app and the
        /// standalone ScreenshotTool share the same settings.json
        /// so the user's preferred folder persists across both
        /// entry points.
        /// </summary>
        public string? DefaultScreenshotSaveDirectory { get; set; }

        /// <summary>
        /// P5: persisted in settings.json. Gates whether the
        /// screenshot editor's OCR button opens the result
        /// window (true) or just copies to clipboard (false).
        /// Default false.
        /// </summary>
        public bool EditorOcrShowWindow { get; set; }

        /// <summary>
        /// User-customised keyboard-shortcut bindings. Only
        /// overrides are stored here; the canonical defaults
        /// live in <see cref="SettingsStore.DefaultShortcuts"/>.
        /// </summary>
        public Dictionary<string, string>? Shortcuts { get; set; }

        /// <summary>
        /// HKCU\...\Run\ApertureNeo toggle. Mirrored into the
        /// registry by <see cref="AutoStartService"/> when the
        /// user changes it in the settings panel.
        /// </summary>
        public bool AutoStart { get; set; }

        /// <summary>
        /// Whether the first-run onboarding dialog has been
        /// shown. Once true, never shown again.
        /// </summary>
        public bool FirstRunShown { get; set; }

        /// <summary>
        /// When true, the main window's close button hides to
        /// the tray instead of exiting. Default true on new
        /// installs; toggled via the settings panel.
        /// </summary>
        public bool CloseToTray { get; set; } = true;

        /// <summary>
        /// True once the user has toggled a plugin (set
        /// <see cref="EnabledPlugins"/> explicitly). Lets the
        /// first-run bootstrap decide whether to opt a fresh
        /// user into the default "all plugins enabled" state
        /// or honour their persisted choice. Defaults to
        /// false for a brand-new install.
        /// </summary>
        public bool PluginChoicesTouched { get; set; }
    }
}
