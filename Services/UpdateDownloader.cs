using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace ApertureNeo.Services;

/// <summary>
/// Downloads the installer to a temp file with progress reporting
/// and cancel support. Returns the path to the downloaded file —
/// the caller is responsible for launching it.
/// </summary>
public sealed class UpdateDownloader
{
    public sealed record DownloadResult(bool Success, string FilePath, string? Error);

    /// <param name="url">GitHub asset browser_download_url</param>
    /// <param name="totalBytes">Expected file size for percent calc (from the asset metadata)</param>
    /// <param name="progress">Reports bytes downloaded so far</param>
    /// <param name="ct">Cancel token — checked before each chunk read</param>
    public async Task<DownloadResult> DownloadAsync(
        string url, long totalBytes,
        IProgress<long> progress,
        CancellationToken ct)
    {
        // Use a timestamped filename so re-running a download after
        // a previous failure doesn't collide with the old partial.
        var tempPath = Path.Combine(
            Path.GetTempPath(),
            $"ApertureNeo-Update-{DateTime.Now:yyyyMMdd-HHmmss}.exe");
        try
        {
            // Per-call HttpClient so the timeout (5min) doesn't
            // affect other network code. Use ResponseHeadersRead so
            // the stream copy doesn't buffer the whole installer in
            // memory before we can report progress.
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            await using var src = await response.Content.ReadAsStreamAsync(ct);
            await using var dst = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None);

            var buffer = new byte[81920];
            long downloaded = 0;
            int read;
            while ((read = await src.ReadAsync(buffer, ct)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, read), ct);
                downloaded += read;
                progress.Report(downloaded);
            }
            return new DownloadResult(true, tempPath, null);
        }
        catch (OperationCanceledException)
        {
            TryDelete(tempPath);
            return new DownloadResult(false, tempPath, "已取消");
        }
        catch (Exception ex)
        {
            TryDelete(tempPath);
            return new DownloadResult(false, tempPath, ex.Message);
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
