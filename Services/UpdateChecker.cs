using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ApertureNeo.Services;

/// <summary>
/// Queries GitHub Releases for the latest main-app version.
///
/// Static lifecycle: one shared HttpClient (per-process) so DNS
/// resolves once, the connection pool is reused, and the 5-second
/// timeout doesn't reset per call. Cache lives per-instance: each
/// About window creates its own UpdateChecker so the cache is
/// scoped to that window's session.
/// </summary>
public sealed class UpdateChecker
{
    // Repo coordinates come from Installer/installer.iss line 15-17.
    private const string Owner = "DuJunxi1993";
    private const string Repo = "Aperture-Neo";
    private const string ReleasesUrl =
        $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";

    private static readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(5),
    };

    static UpdateChecker()
    {
        _http.DefaultRequestHeaders.Add("User-Agent", "ApertureNeo-UpdateChecker");
        _http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public Version CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0);

    private UpdateCheckResult? _cachedMainAppResult;

    /// <summary>
    /// Check GitHub Releases for a newer main app version. Caches
    /// the result for the session so the badge persists and we
    /// don't burn API rate limits on every menu open.
    /// </summary>
    public async Task<UpdateCheckResult> CheckMainAppUpdateAsync(CancellationToken ct = default)
    {
        if (_cachedMainAppResult != null) return _cachedMainAppResult;
        try
        {
            var json = await _http.GetStringAsync(ReleasesUrl, ct);
            var info = ParseMainAppRelease(json);
            _cachedMainAppResult = info != null
                ? UpdateCheckResult.Ok(info)
                : UpdateCheckResult.Failed("解析版本信息失败");
        }
        catch (Exception ex)
        {
            // Network failure: don't bother the user with an error
            // popup. The About window can show "检查失败" if it
            // actively triggered the check.
            _cachedMainAppResult = UpdateCheckResult.Failed(ex.Message);
        }
        return _cachedMainAppResult;
    }

    /// <summary>
    /// Force a re-check (e.g. user clicked "重新检查" in the About
    /// window). Same logic but bypasses the cache.
    /// </summary>
    public async Task<UpdateCheckResult> ForceCheckMainAppUpdateAsync(CancellationToken ct = default)
    {
        _cachedMainAppResult = null;
        return await CheckMainAppUpdateAsync(ct);
    }

    /// <summary>For tests: reset cache.</summary>
    public void ResetCache() => _cachedMainAppResult = null;

    private static UpdateInfo? ParseMainAppRelease(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        var version = ParseVersion(tag);
        if (version == null) return null;
        var notes = root.GetProperty("body").GetString() ?? "";
        var prerelease = root.GetProperty("prerelease").GetBoolean();

        // Find the setup exe asset. GitHub attaches multiple
        // assets to a release (installer, portable zip, plugin
        // zip, debug symbols, etc.) — we want the one starting
        // with ApertureNeo-Setup- and ending in .exe.
        string url = "";
        long size = 0;
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString() ?? "";
            if (name.StartsWith("ApertureNeo-Setup-") && name.EndsWith(".exe"))
            {
                url = asset.GetProperty("browser_download_url").GetString() ?? "";
                size = asset.GetProperty("size").GetInt64();
                break;
            }
        }
        if (string.IsNullOrEmpty(url)) return null;
        return new UpdateInfo(version, notes, url, size, prerelease);
    }

    private static Version? ParseVersion(string tag)
    {
        var s = tag.TrimStart('v', 'V');
        return Version.TryParse(s, out var v) ? v : null;
    }
}
