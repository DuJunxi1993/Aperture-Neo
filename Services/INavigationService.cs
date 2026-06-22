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

    /// <summary>Asynchronously enumerate <paramref name="folderPath"/> and replace the current list.</summary>
    void LoadFolder(string folderPath);

    /// <summary>Load <paramref name="filePath"/>'s parent folder and set the current image to that file.</summary>
    void NavigateTo(string filePath);

    /// <summary>Advance the current image, wrapping at the end. Returns true if the index changed.</summary>
    bool MoveNext();

    /// <summary>Step back the current image, wrapping at the start. Returns true if the index changed.</summary>
    bool MovePrevious();

    /// <summary>Set the current image to <paramref name="index"/>. Returns true if the index changed.</summary>
    bool MoveTo(int index);
}