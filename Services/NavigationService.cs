using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using ApertureNeo.Helpers;
using ApertureNeo.Models;

namespace ApertureNeo.Services;

/// <summary>
/// Owns the current folder's image list and the "current image"
/// pointer. Folder enumeration runs on a worker task; the
/// resulting <see cref="ImageItem"/> list is published back to
/// the UI thread via <see cref="CollectionChanged"/>. A
/// <see cref="FileSystemWatcher"/> reloads on file
/// create/delete/rename in the watched folder.
///
/// Round Z: owns the browse history used by the tree panel's
/// back/forward chips. Explorer-style "visit list + cursor"
/// model (see <see cref="RecordVisit"/>), replacing the two
/// stacks that used to live inside <see cref="FolderTreeView"/>
/// — recording here means every navigation source (tree click,
/// favorites/recent jump, page-up/down, Ctrl+O, drag-drop)
/// funnels through <see cref="LoadFolder"/> and lands in
/// history uniformly.
/// </summary>
public class NavigationService : INavigationService
{
    private readonly ObservableCollection<ImageItem> _items = new();
    private int _currentIndex = -1;
    private string _currentFolder = "";
    private FileSystemWatcher? _watcher;
    private const int MaxHistoryDepth = 100;

    /// <summary>
    /// Sentinel for the "home" view (the folder-tree root:
    /// Favorites / Recent / ThisPC) as a browse-history visit.
    /// Lives at index 0 of <see cref="_visits"/> so Back from the
    /// first real folder returns to home — the file-tree
    /// analogue of Explorer's "Desktop" first position. Empty
    /// strings can't collide with real folder paths. Back/Forward
    /// return it to the caller (MainWindow routes it to
    /// <c>FolderTreeView.ReturnToRoot</c>); it is never passed to
    /// <see cref="LoadFolderCore"/>.
    /// </summary>
    public const string HomeFolder = "";

    /// <summary>Visit list seeded with the home sentinel at index 0
    /// (so Back always has a home to return to); cursor starts at
    /// home. Real folder visits append beyond it and the cap
    /// trimming never touches index 0.</summary>
    private readonly List<string> _visits = new() { HomeFolder };
    private int _cursor = 0;
    // P0 fix: cancel any in-flight enumeration when the user
    // navigates to a new folder. Without this, two rapid
    // folder-switches could leave Items[0..N-1] from folder A
    // and Items[N..M-1] from folder B mixed in the same list, with
    // _currentIndex pointing at what the second callback thought
    // was index 0 (but is actually N in the merged list). The
    // callback checks IsCancellationRequested before adding items.
    private CancellationTokenSource? _loadCts;
    private DispatcherTimer? _fswDebounceTimer;

    public event Action? CollectionChanged;
    public event Action<ImageItem>? CurrentImageChanged;
    public event Action? HistoryChanged;

    public int Count => _items.Count;
    public int CurrentIndex => _currentIndex;
    public ImageItem? Current => _currentIndex >= 0 && _currentIndex < _items.Count ? _items[_currentIndex] : null;
    public IReadOnlyList<ImageItem> Items => _items;
    public string CurrentFolder => _currentFolder;

    public bool CanGoBack => _cursor > 0;
    public bool CanGoForward => _cursor >= 0 && _cursor < _visits.Count - 1;

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
    public void LoadFolder(string folderPath, string? selectFile = null, bool recordHistory = true)
    {
        if (recordHistory) RecordVisit(folderPath);
        LoadFolderCore(folderPath, selectFile, fallbackIndex: -1);
    }

    /// <summary>
    /// Record <paramref name="path"/> as a browse-history visit using
    /// the Explorer-style "visit list + cursor" model:
    ///   - the cursor marks the folder currently shown;
    ///   - index 0 is always the <see cref="HomeFolder"/> sentinel, so
    ///     Back from the first real visit returns to the root view;
    ///   - re-visiting the folder at the cursor is a no-op (dedup);
    ///   - any other visit truncates the forward tail (entries after the
    ///     cursor) and appends the path — mirroring Explorer's rule that
    ///     taking a new path kills the redo trail;
    ///   - "back" / "forward" move the cursor (see
    ///     <see cref="GoBack"/> / <see cref="GoForward"/>) — this is what
    ///     makes "Up one level, then Back" return to the child directory:
    ///     Up records the parent as a fresh visit, so Back lands on the
    ///     child the user just came from.
    /// The list is capped at <see cref="MaxHistoryDepth"/> real entries
    /// (oldest dropped, home sentinel kept) to keep memory predictable
    /// on long browsing sessions.
    /// </summary>
    private void RecordVisit(string path)
    {
        if (string.IsNullOrEmpty(path)) return;

        if (_cursor >= 0 && _cursor < _visits.Count
            && _visits[_cursor].Equals(path, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        bool canForwardBefore = CanGoForward;
        bool canBackBefore = CanGoBack;

        if (_cursor >= 0 && _cursor < _visits.Count - 1)
        {
            _visits.RemoveRange(_cursor + 1, _visits.Count - _cursor - 1);
        }
        _visits.Add(path);
        _cursor = _visits.Count - 1;

        if (_visits.Count > MaxHistoryDepth + 1)
        {
            // Drop the oldest REAL visit (index 1) — the home
            // sentinel at index 0 must survive so Back can always
            // return to the root view.
            _visits.RemoveAt(1);
            _cursor--;
        }

        if (canForwardBefore != CanGoForward || canBackBefore != CanGoBack)
            HistoryChanged?.Invoke();
    }

    /// <summary>
    /// Explorer-style back. Moves the history cursor one visit backwards
    /// and loads that folder WITHOUT re-recording it (the restore is the
    /// consequence of the user pressing Back, not a fresh navigation).
    /// The folder-tree panel combines this with a tree re-drill so the
    /// restored folder is visible and highlighted in the sidebar.
    /// </summary>
    public string? GoBack()
    {
        if (!CanGoBack) return null;
        _cursor--;
        HistoryChanged?.Invoke();
        var target = _visits[_cursor];
        // Home sentinel: the caller (MainWindow) restores the root
        // tree view itself; nothing to load.
        if (target.Length > 0) LoadFolder(target, recordHistory: false);
        return target;
    }

    /// <summary>
    /// Explorer-style forward: mirror of <see cref="GoBack"/>. Moves the
    /// cursor one visit forwards and loads that folder without recording.
    /// The home sentinel can never sit ahead of the cursor (it's at index
    /// 0), so Forward always lands on a real folder.
    /// </summary>
    public string? GoForward()
    {
        if (!CanGoForward) return null;
        _cursor++;
        HistoryChanged?.Invoke();
        var target = _visits[_cursor];
        if (target.Length > 0) LoadFolder(target, recordHistory: false);
        return target;
    }

    /// <summary>
    /// Re-enumerate the currently loaded folder and re-select the
    /// current image. Unlike <see cref="LoadFolder"/>, this is a
    /// *refresh* of the same folder (FileSystemWatcher-triggered):
    /// the invariant is that a refresh never loses the current
    /// image. If the current file was deleted, the item at the old
    /// index is selected instead (enumeration order is stable, so
    /// the old index points at the file that took its place); only
    /// if the folder is now empty does the selection reset.
    /// </summary>
    public void ReloadCurrentFolder()
    {
        if (_currentFolder.Length == 0) return;
        // Capture before LoadFolderCore clears _items.
        string? keep = Current?.FilePath;
        int fallback = _currentIndex;
        DebugLog.Write("Nav", $"ReloadCurrentFolder: {_currentFolder} keep={keep ?? "(none)"} fallback={fallback}");
        LoadFolderCore(_currentFolder, keep, fallback);
    }

    private void LoadFolderCore(string folderPath, string? selectPath, int fallbackIndex)
    {
        if (!Directory.Exists(folderPath)) return;

        // P0 fix: cancel the in-flight enumeration, if any. The
        // background task's BeginInvoke callback checks
        // IsCancellationRequested before mutating _items, so the
        // older enumeration becomes a no-op even if it was already
        // past the directory enumeration step. Without this, two
        // rapid LoadFolder calls would race on _items / _currentIndex
        // and produce a mixed list.
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;

        _currentFolder = folderPath;
        DebugLog.Write("Nav", $"LoadFolder: {folderPath} select={selectPath ?? "(none)"}");

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
                // Bail if a newer LoadFolder cancelled us between
                // the enumeration and this dispatcher dispatch.
                if (ct.IsCancellationRequested) return;

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
                    if (selectPath != null)
                    {
                        var idx = _items.IndexOfFirst(selectPath);
                        // Prefer the requested file; if it no longer
                        // exists (deleted between dialog and reload),
                        // fall back to the old index (stable
                        // enumeration order), then to the first item.
                        _currentIndex = idx >= 0 ? idx
                            : (fallbackIndex >= 0 && fallbackIndex < _items.Count) ? fallbackIndex
                            : (_items.Count > 0 ? 0 : -1);
                    }
                    else
                    {
                        _currentIndex = _items.Count > 0 ? 0 : -1;
                    }
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
        if (!FormatHelper.IsSupported(path)) return;
        if (_fswDebounceTimer == null)
        {
            _fswDebounceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(300),
                IsEnabled = false,
            };
            _fswDebounceTimer.Tick += (_, _) =>
            {
                _fswDebounceTimer.Stop();
                // Refresh, don't navigate: preserve the current image.
                ReloadCurrentFolder();
            };
        }
        _fswDebounceTimer.Stop();
        _fswDebounceTimer.Start();
    }

    public void Dispose()
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _watcher?.Dispose();
    }
}

/// <summary>
/// Extension methods for <see cref="ObservableCollection{T}"/> used
/// by the navigation service.
/// </summary>
internal static class ObservableCollectionExtensions
{
    /// <summary>
    /// Find the first index whose <see cref="ImageItem.FilePath"/>
    /// matches <paramref name="filePath"/> (case-insensitive). Returns
    /// -1 if no match. Used by <see cref="NavigationService.NavigateTo"/>
    /// to map a file path to a list index.
    /// </summary>
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
