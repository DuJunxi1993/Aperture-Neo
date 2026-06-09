using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ApertureNeo.Helpers;

public static class FormatHelper
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".tif",
        ".webp", ".heic", ".heif", ".avif", ".ico", ".wbmp"
    };

    public static bool IsSupported(string path) =>
        SupportedExtensions.Contains(Path.GetExtension(path));

    public static string[] GetSupportedFiles(string folderPath) =>
        Directory.EnumerateFiles(folderPath)
                 .Where(IsSupported)
                 .ToArray();

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

    public static string Filter => "图片文件|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tiff;*.tif;*.webp;*.heic;*.heif;*.avif|所有文件|*.*";
}