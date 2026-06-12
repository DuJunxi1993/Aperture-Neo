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

    public ThumbnailLoadCoordinator(int size = 256, int maxConcurrent = 2)
    {
        Size = size;
        MaxConcurrent = maxConcurrent;
        _semaphore = new SemaphoreSlim(maxConcurrent, maxConcurrent);
    }

    /// <summary>
    /// How many items around the current index to eagerly preload. Items
    /// beyond this window are loaded lazily as they scroll into view.
    /// </summary>
    public int PriorityWindow { get; set; } = 50;

    /// <summary>
    /// Round 68: the WPF BitmapImage is asked to decode only this many
    /// pixels along the long edge, even though the source JPEG may
    /// be larger. WPF scales internally and allocates just the
    /// requested pixel buffer — a 256×256 source JPEG decoded at
    /// DecodePixelWidth=128 yields a 128×128 in-memory bitmap
    /// (64 KB) instead of the full 256×256 (256 KB). The on-screen
    /// cell is ~100-200px so the smaller buffer is visually
    /// indistinguishable.
    /// </summary>
    public int DecodePixelWidth { get; set; } = 128;

    public void LoadForFolder(IEnumerable<ImageItem> items, int currentIndex, Action<string>? onError = null)
    {
        Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        // Priority-load items near the current index; the rest are loaded
        // on demand by EnsureVisible().
        var snapshot = items.ToList();
        var priority = snapshot.Select((it, idx) => (it, dist: Math.Abs(idx - currentIndex)))
                          .Where(x => x.dist <= PriorityWindow)
                          .OrderBy(x => x.dist)
                          .Select(x => x.it)
                          .ToList();
        var remaining = snapshot.Select((it, idx) => (it, dist: Math.Abs(idx - currentIndex)))
                           .Where(x => x.dist > PriorityWindow)
                           .OrderBy(x => x.dist)
                           .Select(x => x.it)
                           .ToList();
        _allRemaining = remaining;
        _currentFocusIndex = currentIndex;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.WhenAll(priority.Select(item => LoadOneAsync(item, ct, onError)));
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { DebugLog.Write("Thumb", "coordinator exception", ex); }
        }, ct);
    }

    /// <summary>
    /// Round 68: focused navigation (Next/Prev/click-to-jump). Unlike
    /// <see cref="LoadForFolder"/>, this does NOT cancel the in-flight
    /// priority queue or restart it — any thumbnails already
    /// scheduled keep loading, the new focus index just becomes the
    /// anchor for subsequent EnsureVisible() calls. This avoids
    /// the 100+ ms hit of resetting the whole priority list every
    /// time the user presses an arrow key.
    /// </summary>
    public void MoveTo(int newIndex)
    {
        if (_allRemaining == null || _allRemaining.Count == 0) return;
        _currentFocusIndex = newIndex;
        // Trigger an EnsureVisible sweep for the new focus.
        var span = Math.Min(2, PriorityWindow / 6);
        EnsureVisible(newIndex - span, newIndex + span);
    }

    private List<ImageItem> _allRemaining = new();
    private int _currentFocusIndex;

    /// <summary>
    /// Ensure thumbnails are loaded for the items in the visible range.
    /// Called from the thumbnail panel when it scrolls / virtualizes.
    /// Round 68: debounced to 100ms — the ScrollChanged event can
    /// fire every frame during a fling scroll, and each call spins
    /// up a fresh Task.Run. Throttling to 10Hz keeps the worker pool
    /// busy with actual decode work instead of being saturated by
    /// scrolling-triggered work that supersedes itself.
    /// </summary>
    private DateTime _lastEnsureCall = DateTime.MinValue;
    private static readonly TimeSpan EnsureDebounce = TimeSpan.FromMilliseconds(100);

    public void EnsureVisible(int firstIndex, int lastIndex)
    {
        if (_allRemaining == null || _allRemaining.Count == 0) return;
        var now = DateTime.UtcNow;
        if (now - _lastEnsureCall < EnsureDebounce) return;
        _lastEnsureCall = now;

        var ct = _cts?.Token ?? CancellationToken.None;
        var pending = new List<ImageItem>();
        for (int i = firstIndex; i <= lastIndex && i < _allRemaining.Count; i++)
        {
            var item = _allRemaining[i];
            if (item != null && item.Thumbnail == null && !item.HasThumbnailError)
                pending.Add(item);
        }
        if (pending.Count == 0) return;
        _ = Task.Run(async () =>
        {
            foreach (var item in pending)
            {
                if (ct.IsCancellationRequested) break;
                await LoadOneAsync(item, ct, null);
            }
        });
    }

    private async Task LoadOneAsync(ImageItem item, CancellationToken ct, Action<string>? onError)
    {
        if (item == null || item.Thumbnail != null) return;
        try { await _semaphore.WaitAsync(ct); }
        catch (OperationCanceledException) { return; }
        try
        {
            // Pass Size=0 so ThumbnailCache uses the size provider
            // (AutoFitPanel.ActualItemWidth).
            var (bytes, error, w, h) = await App.ThumbnailCache.GetOrCreateWithErrorAsync(item.FilePath, 0, ct);
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
            // Round 68: hand the authoritative decoded dimensions to
            // the ImageItem so it can skip its own lazy SKCodec
            // header probe. Skips one FileStream + SKCodec.Create
            // (~1ms) per item — 1s saved on a 1000-image folder.
            if (w > 0 && h > 0)
            {
                item.SetDimensions(w, h);
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
                    // Round 68: ask WPF to decode only DecodePixelWidth
                    // pixels along the long edge, regardless of source
                    // size. With DecodePixelWidth=128 the decoded
                    // pixel buffer is 128×128×4 = 64 KB instead of
                    // 256×256×4 = 256 KB.
                    if (DecodePixelWidth > 0)
                        bmp.DecodePixelWidth = DecodePixelWidth;
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
