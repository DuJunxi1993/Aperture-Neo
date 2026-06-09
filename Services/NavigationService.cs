using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
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

    public void LoadFolder(string folderPath)
    {
        if (!Directory.Exists(folderPath)) return;

        _currentFolder = folderPath;
        var files = FormatHelper.GetSupportedFiles(folderPath);

        foreach (var item in _items) item.Thumbnail = null;
        _items.Clear();
        foreach (var f in files) _items.Add(new ImageItem(f));
        _currentIndex = _items.Count > 0 ? 0 : -1;

        if (_watcher != null)
        {
            _watcher.Path = folderPath;
            _watcher.EnableRaisingEvents = true;
        }

        CollectionChanged?.Invoke();
        if (Current != null)
            CurrentImageChanged?.Invoke(Current);
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
