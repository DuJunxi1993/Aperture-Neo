using System;
using System.Threading;
using System.Threading.Tasks;

namespace ApertureNeo.Services;

/// <summary>
/// SQLite-backed cache for thumbnail JPEGs. Keyed by (path, mtime)
/// with LRU eviction. The concrete <see cref="ThumbnailCache"/>
/// implementation falls back to a memory-only decode + return on
/// SQLite IO errors (logged via <see cref="DebugLog"/>) so a
/// corrupt cache never blocks the UI.
/// </summary>
public interface IThumbnailCache
{
    /// <summary>
    /// Inject a per-call thumbnail size provider. The coordinator
    /// (which knows the actual column width) calls this once at
    /// layout time so cache entries always match the on-screen
    /// target size, avoiding the GPU rescale cost when the
    /// display size drifts.
    /// </summary>
    void SetSizeProvider(Func<int> sizeProvider);

    /// <summary>Get or create a thumbnail as JPEG bytes. Returns null on IO/format errors.</summary>
    Task<byte[]?> GetOrCreateAsync(string path, int size = 256, CancellationToken ct = default);

    /// <summary>
    /// Same as <see cref="GetOrCreateAsync"/> but returns the
    /// decoded dimensions alongside the bytes so the caller can
    /// size its container to the aspect ratio without re-decoding.
    /// </summary>
    Task<(byte[]? data, string? error, int width, int height)> GetOrCreateWithErrorAsync(
        string path, int size = 256, CancellationToken ct = default);

    /// <summary>Drop the cache entry for <paramref name="path"/> (next read will re-decode).</summary>
    Task InvalidateAsync(string path, CancellationToken ct = default);

    /// <summary>Drop every entry (next reads re-decode).</summary>
    Task ClearAsync(CancellationToken ct = default);
}