using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ApertureNeo.Models;

namespace ApertureNeo.Services;

/// <summary>
/// Image decoder that tries the primary engine first and falls
/// back to a secondary engine when the primary cannot decode a
/// file (unsupported format, corrupt data, etc.). Benchmark-driven
/// ordering: SkiaSharp (libjpeg-turbo) is ~4x faster than WIC for
/// JPEG decode with no slow-path spikes, so it runs first; WIC
/// covers formats SkiaSharp can't handle (HEIC, TIFF, RAW, ...).
/// </summary>
public sealed class CompositeImageLoader : IImageLoader
{
    private readonly IImageLoader _primary;
    private readonly IImageLoader _fallback;

    public CompositeImageLoader(IImageLoader primary, IImageLoader fallback)
    {
        _primary = primary;
        _fallback = fallback;
    }

    public int MaxDecodeDimension
    {
        get => _primary.MaxDecodeDimension;
        set => _primary.MaxDecodeDimension = value;
    }

    public Task<ImageLoadResult> LoadAsync(string path, CancellationToken ct = default)
        => LoadAsync(path, 0, 0, ct);

    public async Task<ImageLoadResult> LoadAsync(string path, int targetW, int targetH, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = await _primary.LoadAsync(path, targetW, targetH, ct).ConfigureAwait(false);
        if (result.IsSuccess)
        {
            sw.Stop();
            DebugLog.Write("Loader", $"Skia: {Path.GetFileName(path)} -> {result.Width}x{result.Height} ({sw.ElapsedMilliseconds}ms)");
            return result;
        }
        if (ct.IsCancellationRequested) return result;
        result.Bitmap?.Dispose();
        sw.Restart();
        var fallback = await _fallback.LoadAsync(path, targetW, targetH, ct).ConfigureAwait(false);
        sw.Stop();
        DebugLog.Write("Loader", $"WIC fallback: {Path.GetFileName(path)} ({sw.ElapsedMilliseconds}ms, primary err: {result.ErrorMessage})");
        return fallback;
    }
}
