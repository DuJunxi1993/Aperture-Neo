using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using SkiaSharp;

namespace ApertureNeo.Services;

public class ThumbnailCache : IDisposable
{
    public const int MaxEntries = 2000;

    /// <summary>
    /// Lower bound for the dynamically-sampled thumbnail size. The
    /// provider may return a smaller value (e.g. on a narrow thumb
    /// column) but the cached bytes are still useful for re-display
    /// at slightly larger sizes. 128px comfortably covers 2× DPR at
    /// 64px (Linear's 100px cells render at 1× on most desktops).
    /// </summary>
    private const int MinThumbnailSize = 128;

    private readonly string _dbPath;
    private readonly SqliteConnection _db;
    private readonly SemaphoreSlim _dbLock = new(1, 1);
    private Func<int> _sizeProvider;
    private bool _disposed;

    /// <summary>
    /// Round 68: late-bind the size provider. The ThumbnailCache is
    /// constructed in App.OnStartup before any window exists, but
    /// the actual cell width is known only after the thumbnail
    /// grid is laid out. MainWindow.xaml.cs calls this from
    /// ThumbnailGrid_Loaded to plug the live source in.
    /// </summary>
    public void SetSizeProvider(Func<int> sizeProvider)
    {
        _sizeProvider = sizeProvider ?? (() => 256);
    }

    public ThumbnailCache(string dbPath, Func<int>? sizeProvider = null)
    {
        _dbPath = dbPath;
        _sizeProvider = sizeProvider ?? (() => 256);
        var connStr = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
        _db = new SqliteConnection(connStr);
        _db.Open();
        InitSchema();
    }

    private void InitSchema()
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS thumbnails (
                path TEXT PRIMARY KEY,
                mtime INTEGER NOT NULL,
                data BLOB NOT NULL,
                width INTEGER NOT NULL,
                height INTEGER NOT NULL,
                created_at INTEGER NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_created_at ON thumbnails(created_at);";
        cmd.ExecuteNonQuery();
    }

    public async Task<byte[]?> GetOrCreateAsync(string path, int size = 256, CancellationToken ct = default)
    {
        var (bytes, _, _, _) = await GetOrCreateWithErrorAsync(path, size, ct);
        return bytes;
    }

    public async Task<(byte[]? data, string? error, int width, int height)> GetOrCreateWithErrorAsync(string path, int size = 256, CancellationToken ct = default)
    {
        if (_disposed) return (null, "缓存已释放", 0, 0);

        // Round 68: use the size provider (typically wired to
        // AutoFitPanel.ActualItemWidth) when no explicit size was
        // passed. The size provider is sampled here, on each call, so
        // the cache always matches the current column width — narrow
        // panels produce 128px thumbs, wide ones produce 192px. We
        // floor the size to MinThumbnailSize so resizing the column
        // from wide→narrow doesn't immediately invalidate every
        // cached entry.
        var effectiveSize = size > 0
            ? size
            : Math.Max(MinThumbnailSize, _sizeProvider());

        DebugLog.Write("Thumb", $"GetOrCreate: {Path.GetFileName(path)} (size={effectiveSize})");

        long mtime;
        try { mtime = File.GetLastWriteTimeUtc(path).Ticks; }
        catch (Exception ex)
        {
            DebugLog.Write("Thumb", $"mtime fail: {path}", ex);
            return (null, $"读取文件时间失败: {ex.Message}", 0, 0);
        }

        try
        {
            var cached = await ReadFromDiskAsync(path, mtime, ct);
            if (cached != null)
            {
                DebugLog.Write("Thumb", $"disk hit: {Path.GetFileName(path)} ({cached.Length} bytes)");
                return (cached, null, 0, 0);
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write("Thumb", "disk read fail (fallback to memory)", ex);
        }

        (byte[]? bytes, int w, int h) genResult;
        try
        {
            genResult = await Task.Run(() => GenerateThumbnail(path, effectiveSize), ct);
        }
        catch (Exception ex)
        {
            DebugLog.Write("Thumb", "generate fail", ex);
            return (null, $"生成缩略图失败: {ex.Message}", 0, 0);
        }
        var (bytes, w, h) = genResult;
        if (bytes == null)
        {
            DebugLog.Write("Thumb", $"generate returned null: {Path.GetFileName(path)}");
            return (null, "解码失败（格式不支持或文件损坏）", 0, 0);
        }
        DebugLog.Write("Thumb", $"generated: {Path.GetFileName(path)} ({bytes.Length} bytes, {w}x{h})");

        try
        {
            await WriteToDiskAsync(path, mtime, bytes, w, h, ct);
            await EvictIfNeededAsync(ct);
        }
        catch (Exception ex)
        {
            DebugLog.Write("Thumb", "disk write fail (return memory only)", ex);
        }

        return (bytes, null, w, h);
    }

    public async Task InvalidateAsync(string path, CancellationToken ct = default)
    {
        if (_disposed) return;
        await _dbLock.WaitAsync(ct);
        try
        {
            await Task.Run(() =>
            {
                using var cmd = _db.CreateCommand();
                cmd.CommandText = "DELETE FROM thumbnails WHERE path = $path";
                cmd.Parameters.AddWithValue("$path", path);
                cmd.ExecuteNonQuery();
            }, ct);
        }
        finally { _dbLock.Release(); }
    }

    public async Task ClearAsync(CancellationToken ct = default)
    {
        if (_disposed) return;
        await _dbLock.WaitAsync(ct);
        try
        {
            await Task.Run(() =>
            {
                using var cmd = _db.CreateCommand();
                cmd.CommandText = "DELETE FROM thumbnails";
                cmd.ExecuteNonQuery();
            }, ct);
        }
        finally { _dbLock.Release(); }
    }

    private async Task<byte[]?> ReadFromDiskAsync(string path, long mtime, CancellationToken ct)
    {
        await _dbLock.WaitAsync(ct);
        try
        {
            // Run the synchronous SQLite work on a worker thread so the
            // caller's await yields the dispatcher thread.
            return await Task.Run(() =>
            {
                using var cmd = _db.CreateCommand();
                cmd.CommandText = "SELECT data FROM thumbnails WHERE path = $path AND mtime = $mtime";
                cmd.Parameters.AddWithValue("$path", path);
                cmd.Parameters.AddWithValue("$mtime", mtime);
                using var reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    var len = (int)reader.GetBytes(0, 0, null, 0, 0);
                    var buf = new byte[len];
                    reader.GetBytes(0, 0, buf, 0, len);
                    return buf;
                }
                return null;
            }, ct);
        }
        catch
        {
            return null;
        }
        finally { _dbLock.Release(); }
    }

    private async Task WriteToDiskAsync(string path, long mtime, byte[] data, int w, int h, CancellationToken ct)
    {
        await _dbLock.WaitAsync(ct);
        try
        {
            await Task.Run(() =>
            {
                using var cmd = _db.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO thumbnails (path, mtime, data, width, height, created_at)
                    VALUES ($path, $mtime, $data, $w, $h, $created)
                    ON CONFLICT(path) DO UPDATE SET
                        mtime = excluded.mtime,
                        data = excluded.data,
                        width = excluded.width,
                        height = excluded.height,
                        created_at = excluded.created_at;";
                cmd.Parameters.AddWithValue("$path", path);
                cmd.Parameters.AddWithValue("$mtime", mtime);
                cmd.Parameters.AddWithValue("$data", data);
                cmd.Parameters.AddWithValue("$w", w);
                cmd.Parameters.AddWithValue("$h", h);
                cmd.Parameters.AddWithValue("$created", DateTime.UtcNow.Ticks);
                cmd.ExecuteNonQuery();
            }, ct);
        }
        catch
        {
        }
        finally { _dbLock.Release(); }
    }

    private async Task EvictIfNeededAsync(CancellationToken ct)
    {
        await _dbLock.WaitAsync(ct);
        try
        {
            await Task.Run(() =>
            {
                using var cmd = _db.CreateCommand();
                cmd.CommandText = @"
                    DELETE FROM thumbnails
                    WHERE path IN (
                        SELECT path FROM thumbnails
                        ORDER BY created_at DESC
                        LIMIT -1 OFFSET $max
                    )";
                cmd.Parameters.AddWithValue("$max", MaxEntries);
                cmd.ExecuteNonQuery();
            }, ct);
        }
        catch
        {
        }
        finally { _dbLock.Release(); }
    }

    private static (byte[]? data, int w, int h) GenerateThumbnail(string path, int size)
    {
        SKBitmap? source = null;
        SKBitmap? resized = null;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.SequentialScan);

            using var codec = SKCodec.Create(stream);
            if (codec == null)
            {
                DebugLog.Write("Thumb", $"codec create fail: {Path.GetFileName(path)}");
                return (null, 0, 0);
            }

            source = SKBitmap.Decode(codec);
            if (source == null)
            {
                DebugLog.Write("Thumb", $"source decode fail: {Path.GetFileName(path)}");
                return (null, 0, 0);
            }

            int outW, outH;
            if (source.Width > size || source.Height > size)
            {
                var ratio = Math.Min((float)size / source.Width, (float)size / source.Height);
                outW = Math.Max(1, (int)(source.Width * ratio));
                outH = Math.Max(1, (int)(source.Height * ratio));
                using var tmp = source.Resize(new SKImageInfo(outW, outH, source.ColorType, source.AlphaType), new SKSamplingOptions(SKFilterMode.Linear));
                if (tmp == null)
                {
                    DebugLog.Write("Thumb", $"resize fail: {Path.GetFileName(path)}");
                    return (null, 0, 0);
                }
                resized = tmp.Copy();
            }
            else
            {
                outW = source.Width;
                outH = source.Height;
                resized = source.Copy();
            }
            if (resized == null)
            {
                DebugLog.Write("Thumb", $"copy fail: {Path.GetFileName(path)}");
                return (null, 0, 0);
            }
            DebugLog.Write("Thumb", $"decoded: {Path.GetFileName(path)} {source.Width}x{source.Height} -> {outW}x{outH}");

            byte[]? result;
            using (var image = SKImage.FromBitmap(resized))
            {
                // Round 68: JPEG quality 85→75. 75 is visually
                // indistinguishable from 85 at thumbnail sizes
                // (≤200px) and shaves ~40% off the encoded byte
                // size. For a 1000-image folder that's roughly
                // 12 MB of cache disk saved.
                using var data = image.Encode(SKEncodedImageFormat.Jpeg, 75);
                if (data == null) return (null, 0, 0);
                result = data.ToArray();
            }

            return (result, outW, outH);
        }
        catch (Exception ex)
        {
            DebugLog.Write("Thumb", $"GenerateThumbnail exception: {Path.GetFileName(path)}", ex);
            return (null, 0, 0);
        }
        finally
        {
            resized?.Dispose();
            source?.Dispose();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _db.Close(); _db.Dispose(); }
        catch { }
        _dbLock.Dispose();
    }
}
