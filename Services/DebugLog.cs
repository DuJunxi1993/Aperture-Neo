using System;
using System.IO;

namespace ApertureNeo.Services;

/// <summary>
/// Append-only text log at <c>%TEMP%\ApertureNeo\debug.log</c>.
/// Used for runtime diagnostics that don't justify a full UI
/// surface (thumbnail load errors, cache IO failures, render
/// reentrancy events). All write failures are swallowed — the
/// log is a diagnostic aid, not a contract.
/// </summary>
public static class DebugLog
{
    private static readonly string LogPath = Path.Combine(
        Path.GetTempPath(), "ApertureNeo", "debug.log");
    private static readonly object _lock = new();

    /// <summary>Append a single line to the debug log. Each line
    /// is prefixed with the current time and a category tag
    /// (e.g. <c>[15:42:01.234] [Thumb] disk hit: foo.jpg</c>).
    /// Thread-safe via a static lock.</summary>
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

    /// <summary>Append <paramref name="message"/> plus a one-line
    /// summary of <paramref name="ex"/> (type + message) and its
    /// full stack trace. Convenience overload for the common
    /// "log and continue" pattern.</summary>
    public static void Write(string category, string message, Exception ex)
    {
        Write(category, $"{message}: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
    }
}
