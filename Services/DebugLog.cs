using System;
using System.IO;

namespace ApertureNeo.Services;

/// <summary>
/// Append-only text log at <c>%TEMP%\ApertureNeo\debug.log</c>.
/// Used for runtime diagnostics that don't justify a full UI
/// surface (thumbnail load errors, cache IO failures, render
/// reentrancy events).
///
/// Write failures are NOT silently swallowed: if the file write
/// itself throws (file locked, disk full, ACL denial, path
/// oddity, sandboxed OneDrive folder, etc.) the failure is
/// echoed to <see cref="Console.Error"/> wrapped in a
/// <c>[DebugLog-IO-FAIL]</c> tag so the parent terminal (or
/// attached debugger) can still see it. The earlier
/// <c>catch {}</c> policy made every "log this and continue"
/// site a black hole — the original DebugLog contract is
/// preserved (the app never throws on log failure) but
/// visibility is restored.
/// </summary>
public static class DebugLog
{
    private static readonly string LogPath = Path.Combine(
        Path.GetTempPath(), "ApertureNeo", "debug.log");
    private static readonly object _lock = new();
    private static bool _rotated;

    /// <summary>
    /// High-volume per-item trace categories (thumbnail cache
    /// GetOrCreate / generated lines, grid selection-change logging)
    /// write thousands of lines per folder load. They are gated
    /// behind the <c>APERTURE_DEBUG_THUMB=1</c> environment
    /// variable — set it only when diagnosing thumbnail-cache /
    /// selection issues. Failure and lifecycle lines stay always-on
    /// under their own categories (FS / Loader / Viewer / etc.).
    /// </summary>
    private static bool IsCategoryEnabled(string category)
        => (category != "Thumb" && category != "ThumbGrid")
           || Environment.GetEnvironmentVariable("APERTURE_DEBUG_THUMB") == "1";

    /// <summary>Rotate the log once per process when it exceeds
    /// ~1MB: the previous content is moved to <c>debug.log.old</c>
    /// (overwriting any older copy) and a fresh log starts.</summary>
    private static void RotateIfNeeded()
    {
        if (_rotated) return;
        var info = new FileInfo(LogPath);
        if (!info.Exists || info.Length < 1_000_000) return;
        try
        {
            var old = LogPath + ".old";
            if (File.Exists(old)) File.Delete(old);
            File.Move(LogPath, old);
        }
        catch { /* rotate is best-effort; an in-use log stays put */ }
        finally { _rotated = true; }
    }

    /// <summary>Append a single line to the debug log. Each line
    /// is prefixed with the current time and a category tag
    /// (e.g. <c>[15:42:01.234] [Thumb] disk hit: foo.jpg</c>).
    /// Thread-safe via a static lock.</summary>
    public static void Write(string category, string message)
    {
        if (!IsCategoryEnabled(category)) return;
        try
        {
            var dir = Path.GetDirectoryName(LogPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var line = $"[{DateTime.Now:HH:mm:ss.fff}] [{category}] {message}\n";
            lock (_lock)
            {
                RotateIfNeeded();
                File.AppendAllText(LogPath, line);
            }
        }
        catch (Exception ex)
        {
            // Echo to stderr so the diagnostic is still
            // observable when the file write itself fails.
            // (Previously: catch {} — a black hole that masked
            // every hotkey-conflict / capture-NRE / IO error.)
            try { Console.Error.WriteLine($"[DebugLog-IO-FAIL] {category}: {message} → {ex.GetType().Name}: {ex.Message}"); } catch { }
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
