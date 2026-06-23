using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ApertureNeo.Properties;

namespace ApertureNeo.Helpers;

/// <summary>
/// File-extension filters and enumeration helpers for the
/// supported image formats. Single source of truth for
/// "is this file browsable?" and for the OpenFileDialog filter
/// string. Adding a new format means adding it to
/// <see cref="SupportedExtensions"/> here and nothing else.
/// </summary>
public static class FormatHelper
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".tif",
        ".webp", ".heic", ".heif", ".avif", ".ico", ".wbmp"
    };

    /// <summary>True if <paramref name="path"/>'s extension is in
    /// the supported set (case-insensitive). Used as the predicate
    /// for every "browse this file?" check across the app.</summary>
    public static bool IsSupported(string path) =>
        SupportedExtensions.Contains(Path.GetExtension(path));

    /// <summary>Enumerate every supported file in
    /// <paramref name="folderPath"/>, in OS-defined order. Does not
    /// recurse. Returns an empty array if the folder doesn't exist.</summary>
    public static string[] GetSupportedFiles(string folderPath) =>
        Directory.EnumerateFiles(folderPath)
                 .Where(IsSupported)
                 .ToArray();

    /// <summary>True if <paramref name="folderPath"/> contains at
    /// least one supported image. Short-circuits on first match.
    /// Returns false on missing folder or IO error. Used to decide
    /// whether a folder is worth recording as a recent visit.</summary>
    public static bool FolderHasImages(string folderPath)
    {
        try
        {
            if (!Directory.Exists(folderPath)) return false;
            foreach (var f in Directory.EnumerateFiles(folderPath))
                if (IsSupported(f)) return true;
        }
        catch { }
        return false;
    }

    /// <summary>Filter string for <c>Microsoft.Win32.OpenFileDialog</c>.
    /// Two patterns: every supported image format, plus a
    /// catch-all "all files" fallback.</summary>
    public static string Filter => Strings.OpenFileDialogFilter;
}