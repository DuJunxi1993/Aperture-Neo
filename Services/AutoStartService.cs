using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Microsoft.Win32;
using ApertureNeo.Helpers;

namespace ApertureNeo.Services;

/// <summary>
/// Read/write the Windows "auto-start with user logon" registry
/// key. Backed by
/// <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run\ApertureNeo</c>
/// — current-user scope so admin elevation isn't required and the
/// setting roams with the user's profile.
///
/// Implementation notes:
/// <list type="bullet">
///   <item>Quotes the executable path in the registry value so
///         paths with spaces (default Windows install under
///         <c>Program Files</c>) work.</item>
///   <item><see cref="IsEnabled"/> reads the key directly rather
///         than the cached <see cref="ISettingsStore.AutoStart"/>
///         so the UI reflects the real OS state — e.g. if the
///         user disabled it via Task Manager → Startup tab, the
///         toggle here goes back to off.</item>
///   <item>On any registry failure (permission denied, key
///         missing, etc.) the call logs and returns false /
///         throws a typed exception. Callers (settings panel)
///         show a friendly message; we don't crash the app.</item>
/// </list>
/// </summary>
public static class AutoStartService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ApertureNeo";

    /// <summary>True if the auto-start registry value is present
    /// AND points to the running executable.</summary>
    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            if (key == null) return false;
            var value = key.GetValue(ValueName) as string;
            if (string.IsNullOrEmpty(value)) return false;
            // Compare canonical paths so a cosmetic difference
            // (case, trailing slash) doesn't fool the check.
            var exe = GetExePath();
            return string.Equals(
                value.Trim().Trim('"'),
                exe.Trim().Trim('"'),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            DebugLog.Write("AutoStartService", $"IsEnabled failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>Write or delete the auto-start registry value.
    /// Idempotent — calling with the current state is a no-op.</summary>
    /// <param name="enabled">true = install, false = remove.</param>
    public static void SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true)
                ?? throw new InvalidOperationException("无法打开 HKCU Run key");
            if (!enabled)
            {
                if (key.GetValue(ValueName) != null)
                    key.DeleteValue(ValueName, throwOnMissingValue: false);
                return;
            }
            var exe = GetExePath();
            // Quote the path so spaces don't break the command
            // line. Example value:
            //   "C:\Program Files\Aperture Neo\ApertureNeo.exe"
            key.SetValue(ValueName, $"\"{exe}\"", RegistryValueKind.String);
        }
        catch (Exception ex)
        {
            DebugLog.Write("AutoStartService", $"SetEnabled({enabled}) failed: {ex.Message}");
            throw new AutoStartException(
                enabled ? "无法启用开机自启" : "无法禁用开机自启",
                ex);
        }
    }

    /// <summary>Path of the running executable. Falls back to the
    /// process main module when the entry-assembly location is
    /// unavailable (e.g. single-file publish).</summary>
    private static string GetExePath()
    {
        var entry = Assembly.GetEntryAssembly()?.Location;
        if (!string.IsNullOrEmpty(entry) && File.Exists(entry)) return entry;
        using var p = Process.GetCurrentProcess();
        var main = p.MainModule?.FileName;
        if (!string.IsNullOrEmpty(main)) return main!;
        throw new InvalidOperationException("无法确定可执行文件路径");
    }
}

/// <summary>
/// Raised when the registry write/read fails. Wraps the
/// underlying Win32 exception so the settings panel can surface
/// a clean message without leaking stack traces.
/// </summary>
public sealed class AutoStartException : Exception
{
    public AutoStartException(string message, Exception inner) : base(message, inner) { }
}