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

    private readonly string _dbPath;
    private readonly SqliteConnection _db;
    private readonly SemaphoreSlim _dbLock = new(1, 1);
    private bool _disposed;

    public ThumbnailCache(string dbPath)
    {
        _dbPath = dbPath;
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
        var (bytes, _) = await GetOrCreateWithErrorAsync(path, size, ct);
        return bytes;
    }

    public async Task<(byte[]? data, string? error)> GetOrCreateWithErrorAsync(string path, int size = 256, CancellationToken ct = default)
    {
        if (_disposed) return (null, "缓存已释放");

        DebugLog.Write("Thumb", $"GetOrCreate: {Path.GetFileName(path)}");

        long mtime;
        try { mtime = File.GetLastWriteTimeUtc(path).Ticks; }
        catch (Exception ex)
        {
            DebugLog.Write("Thumb", $"mtime fail: {path}", ex);
            return (null, $"读取文件时间失败: {ex.Message}");
        }

        try
        {
            var cached = await ReadFromDiskAsync(path, mtime, ct);
            if (cached != null)
            {
                DebugLog.Write("Thumb", $"disk hit: {Path.GetFileName(path)} ({cached.Length} bytes)");
                return (cached, null);
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write("Thumb", "disk read fail (fallback to memory)", ex);
        }

        (byte[]? bytes, int w, int h) genResult;
        try
        {
            genResult = await Task.Run(() => GenerateThumbnail(path, size), ct);
        }
        catch (Exception ex)
        {
            DebugLog.Write("Thumb", "generate fail", ex);
            return (null, $"生成缩略图失败: {ex.Message}");
        }
        var (bytes, w, h) = genResult;
        if (bytes == null)
        {
            DebugLog.Write("Thumb", $"generate returned null: {Path.GetFileName(path)}");
            return (null, "解码失败（格式不支持或文件损坏）");
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

        return (bytes, null);
    }

    public async Task InvalidateAsync(string path, CancellationToken ct = default)
    {
        if (_disposed) return;
        await _dbLock.WaitAsync(ct);
        try
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "DELETE FROM thumbnails WHERE path = $path";
            cmd.Parameters.AddWithValue("$path", path);
            cmd.ExecuteNonQuery();
        }
        finally { _dbLock.Release(); }
    }

    public async Task ClearAsync(CancellationToken ct = default)
    {
        if (_disposed) return;
        await _dbLock.WaitAsync(ct);
        try
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "DELETE FROM thumbnails";
            cmd.ExecuteNonQuery();
        }
        finally { _dbLock.Release(); }
    }

    private async Task<byte[]?> ReadFromDiskAsync(string path, long mtime, CancellationToken ct)
    {
        await _dbLock.WaitAsync(ct);
        try
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
        }
        catch
        {
        }
        finally { _dbLock.Release(); }
        return null;
    }

    private async Task WriteToDiskAsync(string path, long mtime, byte[] data, int w, int h, CancellationToken ct)
    {
        await _dbLock.WaitAsync(ct);
        try
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
                using var data = image.Encode(SKEncodedImageFormat.Jpeg, 85);
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
