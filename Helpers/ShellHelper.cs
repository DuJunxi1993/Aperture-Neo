using System.Diagnostics;

namespace ApertureNeo.Helpers;

/// <summary>
/// Static helpers for launching Windows shell programs (File
/// Explorer, etc.) from in-app menu actions. All methods
/// silently swallow exceptions — a failed shell launch should
/// never crash the app, since the user can always retry.
/// </summary>
public static class ShellHelper
{
    /// <summary>
    /// Open a new File Explorer window rooted at
    /// <paramref name="folderPath"/>. The folder should exist;
    /// Explorer will show an error dialog on its own if it does
    /// not. Does nothing if <paramref name="folderPath"/> is
    /// null/empty or the launch fails.
    /// </summary>
    public static void OpenFolder(string folderPath)
    {
        if (string.IsNullOrEmpty(folderPath)) return;
        try { Process.Start("explorer.exe", $"\"{folderPath}\""); } catch { }
    }

    /// <summary>
    /// Open a new File Explorer window with
    /// <paramref name="itemPath"/> selected inside its parent
    /// folder. The item should exist; Explorer highlights the
    /// matching name if it does. Does nothing if
    /// <paramref name="itemPath"/> is null/empty or the launch
    /// fails.
    /// </summary>
    public static void RevealInExplorer(string itemPath)
    {
        if (string.IsNullOrEmpty(itemPath)) return;
        try { Process.Start("explorer.exe", $"/select,\"{itemPath}\""); } catch { }
    }
}
