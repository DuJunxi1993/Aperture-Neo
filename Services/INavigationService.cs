using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ApertureNeo.Models;

namespace ApertureNeo.Services;

/// <summary>
/// Owns the current folder's image list and the "current image"
/// pointer. Folder enumeration runs on a worker task; the resulting
/// <see cref="ImageItem"/> list is published back to the UI thread
/// via <see cref="CollectionChanged"/>. A <c>FileSystemWatcher</c>
/// reloads on file create/delete/rename in the watched folder.
/// </summary>
public interface INavigationService
{
    /// <summary>Raised when the current folder's image list is loaded or reloaded.</summary>
    event Action? CollectionChanged;

    /// <summary>Raised when the current image pointer changes (next/prev/navigate).</summary>
    event Action<ImageItem>? CurrentImageChanged;

    /// <summary>Raised when <see cref="CanGoBack"/> / <see cref="CanGoForward"/>
    /// change (a visit was recorded, a back/forward step moved the cursor, or
    /// history was truncated). The folder-tree panel forwards this into the
    /// chip Visibility bindings.</summary>
    event Action? HistoryChanged;

    /// <summary>True when the browse-history cursor is not at the first visit
    /// (there is a folder to go back to).</summary>
    bool CanGoBack { get; }

    /// <summary>True when the browse-history cursor is not at the last visit
    /// (a Back step can be retraced).</summary>
    bool CanGoForward { get; }

    /// <summary>Number of images in the current folder.</summary>
    int Count { get; }

    /// <summary>Index of the current image, or -1 if no folder is loaded.</summary>
    int CurrentIndex { get; }

    /// <summary>Current image, or null if no folder is loaded.</summary>
    ImageItem? Current { get; }

    /// <summary>Backing list of images for the current folder.</summary>
    IReadOnlyList<ImageItem> Items { get; }

    /// <summary>Path of the currently loaded folder (empty before LoadFolder).</summary>
    string CurrentFolder { get; }

    /// <summary>Asynchronously enumerate <paramref name="folderPath"/> and replace the current list.
    /// If <paramref name="selectFile"/> is provided, the enumeration callback selects that file
    /// instead of defaulting to the first image. Use to avoid the race condition between
    /// LoadFolder + NavigateTo on startup. When <paramref name="recordHistory"/> is true (the
    /// default), the folder is recorded as a browse-history visit — every user-initiated
    /// navigation (tree click, favorites/recent jump, page-up/down, Ctrl+O, drag-drop) funnels
    /// through this and is therefore backable. Pass false for restore-style loads (Back/Forward
    /// themselves, startup restore, file-association launch) that must not become history
    /// entries.</summary>
    void LoadFolder(string folderPath, string? selectFile = null, bool recordHistory = true);

    /// <summary>
    /// Resource-manager-style back: move the browse-history cursor one visit
    /// backwards and load that folder (<see cref="LoadFolder"/> with
    /// recordHistory:false — the restore must not re-record itself). Returns
    /// the restored folder path, or null when there is nothing to go back to
    /// (caller then leaves the current folder untouched). The FIRST position
    /// in every session is the home sentinel (<see cref="NavigationService.HomeFolder"/>,
    /// empty string): Back from the first real visit returns "" and does NOT
    /// load anything — the caller restores the root tree view
    /// (<c>FolderTreeView.ReturnToRoot</c>). The folder-tree panel's "back"
    /// chip is driven by <see cref="CanGoBack"/> / <see cref="HistoryChanged"/>.
    /// </summary>
    string? GoBack();

    /// <summary>
    /// Resource-manager-style forward: mirror of <see cref="GoBack"/> — move the
    /// cursor one visit forwards and load that folder. Returns null when idle.
    /// </summary>
    string? GoForward();

    /// <summary>Load <paramref name="filePath"/>'s parent folder and set the current image to that file.</summary>
    void NavigateTo(string filePath);

    /// <summary>Advance the current image, wrapping at the end. Returns true if the index changed.</summary>
    bool MoveNext();

    /// <summary>Step back the current image, wrapping at the start. Returns true if the index changed.</summary>
    bool MovePrevious();

    /// <summary>Set the current image to <paramref name="index"/>. Returns true if the index changed.</summary>
    bool MoveTo(int index);
}