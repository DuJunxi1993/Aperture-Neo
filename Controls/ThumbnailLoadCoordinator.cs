using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ApertureNeo.Models;
using ApertureNeo.Services;

namespace ApertureNeo.Controls;

public class ThumbnailLoadCoordinator : IDisposable
{
    private readonly SemaphoreSlim _semaphore;
    private CancellationTokenSource? _cts;
    private bool _disposed;

    public int MaxConcurrent { get; }
    public int Size { get; }
    public int ThumbnailErrorLimit { get; set; } = 5;

    public ThumbnailLoadCoordinator(int size = 256, int maxConcurrent = 4)
    {
        Size = size;
        MaxConcurrent = maxConcurrent;
        _semaphore = new SemaphoreSlim(maxConcurrent, maxConcurrent);
    }

    public void LoadForFolder(IEnumerable<ImageItem> items, int currentIndex, Action<string> onError = null!)
    {
        Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        var list = items.Select((it, idx) => (it, dist: Math.Abs(idx - currentIndex)))
                        .OrderBy(x => x.dist)
                        .Select(x => x.it)
                        .ToList();
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.WhenAll(list.Select(item => LoadOneAsync(item, ct, onError)));
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { DebugLog.Write("Thumb", "coordinator exception", ex); }
        }, ct);
    }

    private async Task LoadOneAsync(ImageItem item, CancellationToken ct, Action<string> onError)
    {
        if (item == null || item.Thumbnail != null) return;
        try { await _semaphore.WaitAsync(ct); }
        catch (OperationCanceledException) { return; }
        try
        {
            var (bytes, error) = await App.ThumbnailCache.GetOrCreateWithErrorAsync(item.FilePath, Size, ct);
            if (bytes == null)
            {
                DebugLog.Write("Thumb", $"fail: {item.FileName} - {error}");
                await DispatchInvokeAsync(() =>
                {
                    item.HasThumbnailError = true;
                    item.ThumbnailErrorMessage = error;
                    onError?.Invoke($"缩略图失败: {item.FileName} - {error}");
                });
                return;
            }
            if (ct.IsCancellationRequested) return;
            await DispatchInvokeAsync(() =>
            {
                if (ct.IsCancellationRequested) return;
                try
                {
                    var bmp = new BitmapImage();
                    using var ms = new System.IO.MemoryStream(bytes);
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.StreamSource = ms;
                    bmp.EndInit();
                    bmp.Freeze();
                    item.Thumbnail = bmp;
                }
                catch (Exception ex)
                {
                    DebugLog.Write("Thumb", $"BitmapImage fail: {item.FileName}", ex);
                    item.HasThumbnailError = true;
                    item.ThumbnailErrorMessage = ex.Message;
                }
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { DebugLog.Write("Thumb", $"exception: {item.FileName}", ex); }
        finally { _semaphore.Release(); }
    }

    private static Task DispatchInvokeAsync(Action action)
    {
        var app = Application.Current;
        if (app == null) return Task.CompletedTask;
        return app.Dispatcher.InvokeAsync(action, DispatcherPriority.Background).Task;
    }

    public void Cancel()
    {
        var old = _cts;
        if (old == null) return;
        old.Cancel();
        old.Dispose();
        _cts = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Cancel();
        _semaphore.Dispose();
    }
}
