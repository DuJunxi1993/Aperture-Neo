using System;
using System.IO;

namespace ApertureNeo.Services;

public static class DebugLog
{
    private static readonly string LogPath = Path.Combine(
        Path.GetTempPath(), "ApertureNeo", "debug.log");
    private static readonly object _lock = new();

    public static void Write(string category, string message)
    {
        try
        {
            var dir = Path.GetDirectoryName(LogPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var line = $"[{DateTime.Now:HH:mm:ss.fff}] [{category}] {message}\n";
            lock (_lock)
                File.AppendAllText(LogPath, line);
        }
        catch
        {
        }
    }

    public static void Write(string category, string message, Exception ex)
    {
        Write(category, $"{message}: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
    }
}
