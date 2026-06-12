using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using ApertureNeo.Helpers;
using ApertureNeo.Models;

namespace ApertureNeo.Services;

public class NavigationService
{
    private readonly ObservableCollection<ImageItem> _items = new();
    private int _currentIndex = -1;
    private string _currentFolder = "";
    private FileSystemWatcher? _watcher;

    public event Action? CollectionChanged;
    public event Action<ImageItem>? CurrentImageChanged;

    public int Count => _items.Count;
    public int CurrentIndex => _currentIndex;
    public ImageItem? Current => _currentIndex >= 0 && _currentIndex < _items.Count ? _items[_currentIndex] : null;
    public IReadOnlyList<ImageItem> Items => _items;
    public string CurrentFolder => _currentFolder;

    public NavigationService()
    {
        _watcher = new FileSystemWatcher
        {
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
        };
        _watcher.Created += (_, e) => HandleFileChange(e.FullPath);
        _watcher.Deleted += (_, e) => HandleFileChange(e.FullPath);
        _watcher.Renamed += (_, e) =>
        {
            HandleFileChange(e.FullPath);
            HandleFileChange(e.OldFullPath);
        };
        _watcher.EnableRaisingEvents = false;
    }

    /// <summary>
    /// Load all supported images from a folder without blocking the UI
    /// thread. Directory enumeration and ImageItem construction happen on a
    /// worker task; the constructed list is published back to the
    /// ObservableCollection on the UI thread.
    /// </summary>
    public void LoadFolder(string folderPath)
    {
        if (!Directory.Exists(folderPath)) return;

        _currentFolder = folderPath;

        // Clear current items synchronously so the UI updates immediately.
        foreach (var item in _items) item.Thumbnail = null;
        _items.Clear();
        _currentIndex = -1;

        if (_watcher != null)
        {
            _watcher.Path = folderPath;
            _watcher.EnableRaisingEvents = true;
        }

        CollectionChanged?.Invoke();

        // Enumerate + construct ImageItem on a worker thread to avoid
        // blocking the UI on directories with thousands of files.
        // ImageItem construction is filesystem-free (FileSize/LastWriteTime
        // are resolved lazily on first access), so this is fast.
        Task.Run(() =>
        {
            string[] files;
            try { files = FormatHelper.GetSupportedFiles(folderPath); }
            catch { return; }

            var list = new List<ImageItem>(files.Length);
            foreach (var f in files) list.Add(new ImageItem(f));

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null) return;
            dispatcher.BeginInvoke(new Action(() =>
            {
                // Round 68: suppress per-item CollectionChanged during bulk
                // add. With a 1000-image folder the naive foreach+Add
                // fires 1000 CollectionChanged events → 1000 layout passes
                // on the thumbnail grid (which re-measures AutoFitPanel and
                // creates ThumbnailItem containers on every event). The
                // single CollectionChanged we fire at the end lets
                // subscribers refresh once for the whole batch. The
                // field null/restoration is wrapped in try/finally so a
                // subscriber throwing doesn't leave the collection mute.
                var saved = CollectionChanged;
                CollectionChanged = null;
                try
                {
                    foreach (var item in list) _items.Add(item);
                    _currentIndex = _items.Count > 0 ? 0 : -1;
                }
                finally { CollectionChanged = saved; }
                CollectionChanged?.Invoke();
                if (Current != null)
                    CurrentImageChanged?.Invoke(Current);
            }));
        });
    }

    public void NavigateTo(string filePath)
    {
        var idx = _items.IndexOfFirst(filePath);
        if (idx >= 0)
        {
            _currentIndex = idx;
            CurrentImageChanged?.Invoke(Current!);
        }
    }

    public bool MoveNext()
    {
        if (_items.Count == 0) return false;
        _currentIndex = (_currentIndex + 1) % _items.Count;
        CurrentImageChanged?.Invoke(Current!);
        return true;
    }

    public bool MovePrevious()
    {
        if (_items.Count == 0) return false;
        _currentIndex = (_currentIndex - 1 + _items.Count) % _items.Count;
        CurrentImageChanged?.Invoke(Current!);
        return true;
    }

    public bool MoveTo(int index)
    {
        if (index < 0 || index >= _items.Count) return false;
        _currentIndex = index;
        CurrentImageChanged?.Invoke(Current!);
        return true;
    }

    private void HandleFileChange(string path)
    {
        if (FormatHelper.IsSupported(path))
        {
            LoadFolder(_currentFolder);
        }
    }

    public void Dispose()
    {
        _watcher?.Dispose();
    }
}

internal static class ObservableCollectionExtensions
{
    public static int IndexOfFirst(this ObservableCollection<ImageItem> items, string filePath)
    {
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i].FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }
}
