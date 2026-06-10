using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ApertureNeo.Services;
using SkiaSharp;

namespace ApertureNeo.Controls;

public class SkiaImageViewer : FrameworkElement
{
    private SKBitmap? _bitmap;
    private SKBitmap? _oldBitmap;
    private float _oldZoom, _oldOffX, _oldOffY;
    private float _zoom = 1f;
    private float _targetZoom = 1f;
    private float _offsetX, _offsetY;
    private float _targetOffsetX, _targetOffsetY;
    private bool _isDragging;
    private Point _dragStart;
    private float _dragStartOffsetX, _dragStartOffsetY;
    private float _fitScale = 1f;
    private CancellationTokenSource? _loadCts;
    private WriteableBitmap? _wbmp;
    private bool _dirty = true;
    // Cached SK surface + paints — reallocated only when size changes (Fix#3: per-frame alloc)
    private SKSurface? _surface;
    private SKPaint? _paintOld;
    private SKPaint? _paintNew;
    private int _cachedWidth;
    private int _cachedHeight;
    private bool _isPanning;

    private float _animOpacity = 1f;
    private DateTime _animStart;
    private float _animFromZoom, _animFromOffX, _animFromOffY;
    private bool _animating;
    private const float AnimDuration = 0.18f;

    public ImageLoader ImageLoader { get; } = new();

    public event Action<float>? ZoomChanged;
    public event Action<string>? StatusChanged;

    public SkiaImageViewer()
    {
        // Set bitmap scaling mode once (Fix#3: avoid per-paint DP walk)
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);

        Loaded += (_, _) =>
        {
            var parent = VisualTreeHelper.GetParent(this) as FrameworkElement;
            if (parent != null)
                parent.SizeChanged += (_, e) =>
                {
                    InvalidateMeasure();
                    if (!_isDragging && !_animating && _bitmap != null)
                        FitToScreen();
                };
        };
    }

    public float Zoom
    {
        get => _zoom;
        set
        {
            _targetZoom = Math.Clamp(value, 0.05f, 20f);
            StartZoomAnim();
        }
    }

    private void StartZoomAnim()
    {
        _animFromZoom = _zoom;
        _animFromOffX = _offsetX;
        _animFromOffY = _offsetY;
        _animStart = DateTime.UtcNow;
        _animating = true;
        _dirty = true;
        CompositionTarget.Rendering += OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        var elapsed = (float)(DateTime.UtcNow - _animStart).TotalSeconds;
        var t = Math.Clamp(elapsed / AnimDuration, 0f, 1f);
        t = t * t * (3f - 2f * t);

        _zoom = _animFromZoom + (_targetZoom - _animFromZoom) * t;
        _offsetX = _animFromOffX + (_targetOffsetX - _animFromOffX) * t;
        _offsetY = _animFromOffY + (_targetOffsetY - _animFromOffY) * t;

        if (t >= 1f)
        {
            _zoom = _targetZoom;
            _offsetX = _targetOffsetX;
            _offsetY = _targetOffsetY;
            _animating = false;
            CompositionTarget.Rendering -= OnRendering;
        }

        _dirty = true;
        InvalidateVisual();
    }

    private EventHandler? _activeCrossFade;

    public void LoadImage(string path)
    {
        var oldCts = _loadCts;
        if (oldCts != null)
        {
            oldCts.Cancel();
            oldCts.Dispose();
        }
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;

        if (_activeCrossFade != null)
        {
            CompositionTarget.Rendering -= _activeCrossFade;
            _activeCrossFade = null;
        }

        StatusChanged?.Invoke("加载中...");

        Task.Run(async () =>
        {
            var result = await ImageLoader.LoadAsync(path, ct);
            if (ct.IsCancellationRequested) return;

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (ct.IsCancellationRequested) return;

                _oldBitmap?.Dispose();
                _oldBitmap = _bitmap;
                _oldZoom = _zoom;
                _oldOffX = _offsetX;
                _oldOffY = _offsetY;
                _wbmp = null;
                _bitmap = null;

                if (result.IsSuccess && result.Bitmap != null)
                {
                    _bitmap = result.Bitmap;
                    _dirty = true;
                    _animOpacity = 0f;

                    FitToScreen();

                    if (_oldBitmap != null && _targetZoom > 0.1f)
                    {
                        float pulseZoom = _targetZoom * 0.96f;
                        _zoom = pulseZoom;
                        _offsetX = _targetOffsetX + (_bitmap.Width * (_targetZoom - pulseZoom)) / 2f;
                        _offsetY = _targetOffsetY + (_bitmap.Height * (_targetZoom - pulseZoom)) / 2f;
                        StartZoomAnim();
                    }

                    var fadeStart = DateTime.UtcNow;
                    EventHandler? handler = null;
                    handler = (s, e) =>
                    {
                        if (ct.IsCancellationRequested)
                        {
                            if (handler != null) CompositionTarget.Rendering -= handler;
                            _activeCrossFade = null;
                            return;
                        }
                        var ft = (float)(DateTime.UtcNow - fadeStart).TotalSeconds / 0.25f;
                        _animOpacity = Math.Clamp(ft, 0f, 1f);
                        _dirty = true;
                        InvalidateVisual();
                        if (_animOpacity >= 1f)
                        {
                            CompositionTarget.Rendering -= handler;
                            _activeCrossFade = null;
                            _animOpacity = 1f;
                            _oldBitmap?.Dispose();
                            _oldBitmap = null;
                        }
                    };
                    _activeCrossFade = handler;
                    CompositionTarget.Rendering += handler;

                    StatusChanged?.Invoke($"{result.Bitmap.Width}x{result.Bitmap.Height}");
                }
                else
                {
                    StatusChanged?.Invoke($"加载失败: {result.ErrorMessage}");
                }
                InvalidateVisual();
            });
        }, ct);
    }

    public void FitToScreen()
    {
        if (_bitmap == null)
        {
            StatusChanged?.Invoke("无可显示图片");
            return;
        }

        var w = (float)Math.Max(1, ActualWidth);
        var h = (float)Math.Max(1, ActualHeight);
        if (w <= 1 || h <= 1)
        {
            Dispatcher.BeginInvoke(() => FitToScreen());
            return;
        }

        _fitScale = Math.Min(w / _bitmap.Width, h / _bitmap.Height);
        _targetZoom = _fitScale;
        _targetOffsetX = (w - _bitmap.Width * _targetZoom) / 2f;
        _targetOffsetY = (h - _bitmap.Height * _targetZoom) / 2f;
        StartZoomAnim();
        ZoomChanged?.Invoke(_targetZoom);
    }

    public void ZoomToOriginal()
    {
        if (_bitmap == null) return;
        _targetZoom = 1f;
        CenterImage();
        StartZoomAnim();
        ZoomChanged?.Invoke(_targetZoom);
    }

    public void ZoomIn()
    {
        _targetZoom = Math.Clamp(_zoom * 1.6f, 0.05f, 20f);
        CenterImage();
        StartZoomAnim();
        ZoomChanged?.Invoke(_targetZoom);
    }

    public void ZoomOut()
    {
        _targetZoom = Math.Clamp(_zoom * 0.5f, 0.05f, 20f);
        CenterImage();
        StartZoomAnim();
        ZoomChanged?.Invoke(_targetZoom);
    }

    private void CenterImage()
    {
        var w = (float)Math.Max(1, ActualWidth);
        var h = (float)Math.Max(1, ActualHeight);
        if (_bitmap == null || w <= 1 || h <= 1) return;
        _targetOffsetX = (w - _bitmap.Width * _targetZoom) / 2f;
        _targetOffsetY = (h - _bitmap.Height * _targetZoom) / 2f;
    }

    private void RenderToWriteableBitmap()
    {
        if (_bitmap == null && _oldBitmap == null) return;

        var w = Math.Max(1, (int)RenderSize.Width);
        var h = Math.Max(1, (int)RenderSize.Height);

        // Allocate WriteableBitmap and SKSurface only when size changes (Fix#3)
        if (_wbmp == null || _wbmp.PixelWidth != w || _wbmp.PixelHeight != h)
            _wbmp = new WriteableBitmap(w, h, 96, 96, PixelFormats.Pbgra32, null);

        if (_surface == null || _cachedWidth != w || _cachedHeight != h)
        {
            _surface?.Dispose();
            _surface = SKSurface.Create(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
            _cachedWidth = w;
            _cachedHeight = h;
        }

        // Lazy-init paints (Fix#3: avoid per-frame allocation)
        _paintOld ??= new SKPaint { FilterQuality = SKFilterQuality.High, IsAntialias = true };
        _paintNew ??= new SKPaint { FilterQuality = SKFilterQuality.High, IsAntialias = true };

        var canvas = _surface.Canvas;
        // Clear to fully transparent. Skia draws the image with pre-multiplied alpha;
        // we write that pre-multiplied data into a Pbgra32 WriteableBitmap so WPF's
        // compositor respects the alpha channel and lets siblings (FloatingBar) show
        // through in the empty regions.
        canvas.Clear(SKColors.Transparent);

        // Draw old bitmap (fades out, using its original zoom/offset)
        if (_oldBitmap != null)
        {
            byte oldAlpha = (byte)(255 * (1f - _animOpacity));
            _paintOld.Color = new SKColor(255, 255, 255, oldAlpha);
            canvas.Save();
            canvas.Translate(_oldOffX, _oldOffY);
            canvas.Scale(_oldZoom);
            canvas.DrawBitmap(_oldBitmap, 0, 0, _paintOld);
            canvas.Restore();
        }

        // Draw new bitmap (fades in)
        if (_bitmap != null)
        {
            byte alpha = (byte)(255 * _animOpacity);
            _paintNew.Color = new SKColor(255, 255, 255, alpha);
            canvas.Save();
            canvas.Translate(_offsetX, _offsetY);
            canvas.Scale(_zoom);
            canvas.DrawBitmap(_bitmap, 0, 0, _paintNew);
            canvas.Restore();
        }

        using var image = _surface.Snapshot();
        using var pixmap = image.PeekPixels();
        if (pixmap == null) return;

        var srcRect = new Int32Rect(0, 0, w, h);
        var bytes = pixmap.GetPixelSpan();
        _wbmp.Lock();
        unsafe
        {
            fixed (byte* ptr = bytes)
            {
                _wbmp.WritePixels(srcRect, (IntPtr)ptr, bytes.Length, w * 4);
            }
        }
        _wbmp.Unlock();
        _dirty = false;
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (_bitmap == null)
        {
            return;
        }

        if (_dirty || _wbmp == null)
            RenderToWriteableBitmap();

        // Short-circuit: skip if no writeable bitmap (Fix#3: avoid DC.DrawImage null op)
        if (_wbmp != null)
            dc.DrawImage(_wbmp, new Rect(RenderSize));
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        // Force WriteableBitmap to be re-allocated to new size, otherwise
        // it will reuse stale dimensions and clip incorrectly.
        _wbmp = null;
        _dirty = true;
        InvalidateVisual();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        if (_bitmap == null) return;

        var pos = e.GetPosition(this);
        var factor = e.Delta > 0 ? 1.3f : 0.7f;
        _targetZoom = Math.Clamp(_zoom * factor, 0.05f, 20f);

        var worldX = (pos.X - _offsetX) / _zoom;
        var worldY = (pos.Y - _offsetY) / _zoom;

        _targetOffsetX = (float)(pos.X - worldX * _targetZoom);
        _targetOffsetY = (float)(pos.Y - worldY * _targetZoom);

        StartZoomAnim();
        ZoomChanged?.Invoke(_targetZoom);
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (_bitmap == null) return;
        _isDragging = true;
        _isPanning = false;
        _dragStart = e.GetPosition(this);
        _dragStartOffsetX = _offsetX;
        _dragStartOffsetY = _offsetY;

        if (_animating)
        {
            CompositionTarget.Rendering -= OnRendering;
            _animating = false;
        }

        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!_isDragging || _bitmap == null) return;
        var pos = e.GetPosition(this);
        _offsetX = _dragStartOffsetX + (float)(pos.X - _dragStart.X);
        _offsetY = _dragStartOffsetY + (float)(pos.Y - _dragStart.Y);
        _targetOffsetX = _offsetX;
        _targetOffsetY = _offsetY;

        var dx = Math.Abs(pos.X - _dragStart.X);
        var dy = Math.Abs(pos.Y - _dragStart.Y);
        if (dx > 2 || dy > 2) _isPanning = true;

        _dirty = true;
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!_isDragging)
        {
            base.OnMouseLeftButtonUp(e);
            return;
        }
        _isDragging = false;
        ReleaseMouseCapture();

        if (!_isPanning && e.ClickCount == 2 && _bitmap != null)
        {
            if (Math.Abs(_zoom - _fitScale) < 0.01f)
                ZoomToOriginal();
            else
                FitToScreen();
        }

        e.Handled = true;
    }
}