using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ApertureNeo.Models;

namespace ApertureNeo.Services;

public class SettingsStore
{
    public const int MaxRecentCount = 10;

    private static readonly string AppDataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ApertureNeo");
    private static readonly string SettingsPath = Path.Combine(AppDataDir, "settings.json");

    private readonly object _lock = new();
    private readonly List<string> _favorites = new();
    private readonly List<RecentEntry> _recent = new();
    private CancellationTokenSource? _saveCts;
    private int _saveGeneration;

    public IReadOnlyList<string> Favorites
    {
        get { lock (_lock) return _favorites.ToList(); }
    }

    public IReadOnlyList<RecentEntry> Recent
    {
        get { lock (_lock) return _recent.ToList(); }
    }

    public event Action? FavoritesChanged;
    public event Action? RecentChanged;

    public AppTheme Theme { get; set; } = AppTheme.System;

    public void Load()
    {
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
            }
            Theme = data.Theme;
        }
        catch
        {
        }
    }

    public void Save()
    {
        try
        {
            List<string> favs;
            List<RecentEntry> recs;
            AppTheme theme;
            lock (_lock)
            {
                favs = _favorites.ToList();
                recs = _recent.ToList();
                theme = Theme;
            }
            Directory.CreateDirectory(AppDataDir);
            var json = JsonSerializer.Serialize(
                new SettingsData { Favorites = favs, Recent = recs, Theme = theme },
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
        }
    }

    public bool IsFavorite(string path)
    {
        lock (_lock)
            return _favorites.Any(p => p.Equals(path, StringComparison.OrdinalIgnoreCase));
    }

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

    public void RemoveRecent(string path)
    {
        bool changed;
        lock (_lock) { changed = _recent.RemoveAll(p => p.Path.Equals(path, StringComparison.OrdinalIgnoreCase)) > 0; }
        if (changed) { ScheduleSave(); RecentChanged?.Invoke(); }
    }

    public void ClearRecent()
    {
        bool changed;
        lock (_lock) { changed = _recent.Count > 0; _recent.Clear(); }
        if (changed) { ScheduleSave(); RecentChanged?.Invoke(); }
    }

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
        public AppTheme Theme { get; set; } = AppTheme.System;
    }
}
