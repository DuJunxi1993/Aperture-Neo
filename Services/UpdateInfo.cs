using System;

namespace ApertureNeo.Services;

/// <summary>
/// Result of a GitHub release query. Holds everything the About
/// window needs to display the update prompt and start a download.
/// </summary>
public sealed record UpdateInfo(
    Version LatestVersion,
    string ReleaseNotes,
    string DownloadUrl,
    long FileSize,
    bool IsPrerelease)
{
    public bool IsNewerThan(Version current) => LatestVersion > current;
}

/// <summary>
/// Tri-state result of an update check. Skipped = cache hit and
/// the caller didn't ask for a refresh; Ok = we have info (or
/// "no update" intentionally); Failed = the network parse blew
/// up and we surfaced the error so the UI can show it.
/// </summary>
public sealed record UpdateCheckResult(bool Checked, UpdateInfo? Info, string? Error)
{
    public static UpdateCheckResult Ok(UpdateInfo info) => new(true, info, null);
    public static UpdateCheckResult Failed(string error) => new(true, null, error);
    public static UpdateCheckResult Skipped() => new(false, null, null);
}
