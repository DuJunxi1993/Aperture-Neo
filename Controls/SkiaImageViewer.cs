using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using ApertureNeo.Models;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ApertureNeo.Services;
using SkiaSharp;

namespace ApertureNeo.Controls;

/// <summary>
/// Image-rendering surface. Loads via <see cref="ImageLoader"/>
/// (off-thread decode), caches the result in a
/// <see cref="WriteableBitmap"/>, and composites with
/// <see cref="SkiaSharp"/> on <see cref="OnRender"/>. Supports
/// mouse-wheel zoom-to-cursor, pan, fit-to-screen, and
/// animated transitions. Screenshot tools (Windows Snip, etc.)
/// re-enter WPF's render path mid-frame; a reentrancy guard
/// drops the second call to avoid a deadlock.
/// </summary>
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
    // Zoom-triggered quality re-decode: when the user zooms past the
    // base decode size, reload the same file at higher resolution and
    // swap the bitmap in place (preserving zoom/offset). Debounced via
    // _upgradeTimer so rapid wheel zooms only trigger one decode.
    private string? _currentPath;
    private int _sourceW, _sourceH;
    private CancellationTokenSource? _upgradeCts;
    private readonly DispatcherTimer _upgradeTimer;
    private const float UpgradeOversample = 1.25f;
    private const int UpgradeMaxDimension = 7680;
    private WriteableBitmap? _wbmp;
    private bool _dirty = true;
    // Reentrancy guard. Screenshot tools (Windows Snip, PrintWindow with
    // PW_RENDERFULLCONTENT, etc.) can re-enter WPF's OnRender while
    // our SKSurface draw is mid-flight, leading to a deadlock or an
    // invalid memory access on the WriteableBitmap backbuffer. We
    // drop the reentrant call instead.
    private bool _inRender;
    // Cached SK surface + paints — reallocated only when size changes (Fix#3: per-frame alloc)
    private SKSurface? _surface;
    private SKPaint? _paintOld;
    private SKPaint? _paintNew;
    private int _cachedWidth;
    private int _cachedHeight;
    private bool _isPanning;

    private int _rotation;
    private float _animOpacity = 1f;
    private DateTime _animStart;
    private float _animFromZoom, _animFromOffX, _animFromOffY;
    private bool _animating;
    // A viewport resize arrived while a fit animation or drag was in
    // flight. The refit is deferred to the end of the animation/drag
    // instead of being dropped (see HandleViewportResized).
    private bool _pendingRefit;
    private const float AnimDuration = 0.18f;

    /// <summary>
    /// The decoder used to load image files. Defaults to
    /// <see cref="ImageLoader"/> (SkiaSharp CPU decode) as a safe
    /// fallback; the host window's VM replaces it with a WIC-based
    /// loader for hardware-accelerated decode.
    /// </summary>
    public IImageLoader ImageLoader { get; set; } = new ImageLoader();

    public event Action<float>? ZoomChanged;
    public event Action<string>? StatusChanged;
    /// <summary>
    /// Raised when a new image finishes loading. The arg is the
    /// <see cref="ImageLoadResult"/> with authoritative dimensions and the
    /// loaded bitmap. Subscribers can update bound models (e.g. set the
    /// ImageItem's width/height) and refresh related UI (info pill, etc.).
    /// </summary>
    public event Action<ImageLoadResult>? ImageLoaded;

    public SkiaImageViewer()
    {
        // Set bitmap scaling mode once (Fix#3: avoid per-paint DP walk)
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);

        _upgradeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _upgradeTimer.Tick += (_, _) => { _upgradeTimer.Stop(); MaybeUpgradeQuality(); };
        ZoomChanged += _ => MaybeScheduleUpgrade();
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

    /// <summary>Current horizontal offset in device pixels
    /// (the translation applied to the bitmap before scale).</summary>
    public float OffsetX => _offsetX;

    /// <summary>Current vertical offset in device pixels.</summary>
    public float OffsetY => _offsetY;

    /// <summary>Scale factor that fits the bitmap into the current
    /// render size. Zero if no bitmap is loaded.</summary>
    public float FitScale => _fitScale;

    /// <summary>Clockwise rotation in degrees (0 / 90 / 180 / 270).
    /// Setting this triggers a fit-to-screen so the image is
    /// re-centred at the new orientation.</summary>
    public int Rotation
    {
        get => _rotation;
        set
        {
            var r = ((value % 360) + 360) % 360;
            r = (r / 90) * 90;
            if (_rotation == r) return;
            _rotation = r;
            FitToScreen();
        }
    }

    /// <summary>Dimensions of the loaded bitmap considering
    /// the current rotation, or (0,0) if none.</summary>
    public SKSize BitmapSize => _bitmap != null
        ? (_rotation == 90 || _rotation == 270)
            ? new SKSize(_bitmap.Height, _bitmap.Width)
            : new SKSize(_bitmap.Width, _bitmap.Height)
        : SKSize.Empty;

    /// <summary>
    /// Optional overlay bitmap drawn on top of <see cref="_bitmap"/>
    /// with the same zoom and offset. Setting this triggers a
    /// re-render. Used by screenshot/editing tools to composite
    /// user annotations over the source image.
    /// </summary>
    public SKBitmap? OverlayBitmap
    {
        get => _overlayBitmap;
        set
        {
            if (_overlayBitmap != value)
            {
                _overlayBitmap?.Dispose();
                _overlayBitmap = value;
            }
            _dirty = true;
            InvalidateVisual();
        }
    }
    private SKBitmap? _overlayBitmap;

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
        // Drop reentrant calls. CompositionTarget.Rendering normally
        // serializes with OnRender, but screenshot tools can force a
        // second tick while the first is still drawing.
        if (_inRender) return;

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
            FinishPendingRefit();
        }

        _dirty = true;
        InvalidateVisual();
    }

    private EventHandler? _activeCrossFade;

    /// <summary>
    /// Load and display an image file. Decodes at an adaptive
    /// resolution (≈2× viewport size, capped at 4K) so the
    /// GPU / CPU doesn't waste memory decoding an 8K source
    /// when the display can only show 4K.
    /// </summary>
    public void LoadImage(string path)
    {
        CancelPending();
        var ct = _loadCts.Token;
        _currentPath = path;

        double vw = ActualWidth;
        double vh = ActualHeight;
        // If the viewer hasn't been measured yet (e.g. SetViewer
        // runs right at Loaded), decode at source resolution
        // instead of clamping a 0-size viewport to 1080 — that
        // would render the first image visibly soft on hi-DPI.
        int targetW = vw < 10 ? 0 : (int)Math.Clamp(vw * 2, 1080, 3840);
        int targetH = vh < 10 ? 0 : (int)Math.Clamp(vh * 2, 1080, 3840);

        Task.Run(async () =>
        {
            var result = await ImageLoader.LoadAsync(path, targetW, targetH, ct);
            if (ct.IsCancellationRequested) return;

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (ct.IsCancellationRequested) return;
                ApplyResult(result, ct);
            });
        }, ct);
    }

    /// <summary>
    /// Display a pre-decoded image. The caller (typically the VM's
    /// pre-decode cache) has already loaded the SKBitmap; this
    /// method applies it with the same cross-fade + FitToScreen
    /// pipeline as <see cref="LoadImage"/> but without any decode
    /// latency — navigation is instant.
    /// </summary>
    public void LoadPreDecoded(ImageLoadResult result)
    {
        CancelPending();
        var ct = _loadCts.Token;
        ApplyResult(result, ct);
    }

    private void CancelPending()
    {
        var old = _loadCts;
        if (old != null) { old.Cancel(); old.Dispose(); }
        _loadCts = new CancellationTokenSource();

        var upg = _upgradeCts;
        if (upg != null) { upg.Cancel(); upg.Dispose(); _upgradeCts = null; }
        _upgradeTimer.Stop();

        if (_activeCrossFade != null)
        {
            CompositionTarget.Rendering -= _activeCrossFade;
            _activeCrossFade = null;
        }
    }

    private void MaybeScheduleUpgrade()
    {
        if (_bitmap == null || _currentPath == null) return;
        _upgradeTimer.Stop();
        _upgradeTimer.Start();
    }

    private void MaybeUpgradeQuality()
    {
        if (_bitmap == null || _currentPath == null || _sourceW <= 0) return;
        // Wait for any cross-fade to finish; re-check afterwards.
        if (_animOpacity < 1f)
        {
            if (_activeCrossFade != null) _upgradeTimer.Start();
            return;
        }

        int neededW = Math.Min(_sourceW, (int)Math.Ceiling(_sourceW * _targetZoom * UpgradeOversample));
        neededW = Math.Min(neededW, UpgradeMaxDimension);
        if (neededW <= _bitmap.Width * 1.15f) return;

        // Round 71: capture whether the user was at fit scale when
        // the upgrade was scheduled. The upgraded bitmap is larger
        // than the one the fit was computed for — preserving the
        // zoom would blow the image up past the viewport (fullscreen
        // entry followed by a blurry→cropped jump). The refit runs
        // after the swap in ApplyUpgrade.
        bool refitToScreen = IsAtFitScale;

        var old = _upgradeCts;
        if (old != null) { old.Cancel(); old.Dispose(); }
        _upgradeCts = new CancellationTokenSource();
        var ct = _upgradeCts.Token;
        string path = _currentPath;

        _ = Task.Run(async () =>
        {
            var result = await ImageLoader.LoadAsync(path, neededW, 0, ct).ConfigureAwait(false);
            if (ct.IsCancellationRequested || !result.IsSuccess || result.Bitmap == null) return;
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (ct.IsCancellationRequested) return;
                if (_currentPath != path || _bitmap == null || result.Bitmap.Width <= _bitmap.Width)
                {
                    result.Bitmap.Dispose();
                    return;
                }
                ApplyUpgrade(result, refitToScreen);
            });
        }, ct);
    }

    private void ApplyUpgrade(ImageLoadResult result, bool refitToScreen)
    {
        DebugLog.Write("FS", $"ApplyUpgrade: {_bitmap.Width}x{_bitmap.Height} -> {result.Bitmap.Width}x{result.Bitmap.Height} zoom={_zoom:F4} fitScale={_fitScale:F4} viewport={ActualWidth:F0}x{ActualHeight:F0} refit={refitToScreen}");
        // Same image, sharper. Preserve zoom/offset — no cross-fade
        // and no ImageLoaded (dimensions and bound models are
        // unchanged). The old low-res bitmap is not referenced by
        // the cross-fade (_oldBitmap is the previous image), so it
        // can be disposed immediately.
        _bitmap.Dispose();
        _bitmap = result.Bitmap;
        _dirty = true;
        if (refitToScreen && !_isDragging)
        {
            // The fit was computed for the previous (smaller) bitmap;
            // the same zoom on the larger bitmap would overflow the
            // viewport and show a crop. Snap back to the real fit
            // for the new bitmap (no animation — the swap is already
            // a visible change).
            FitToScreenSkipAnimation = true;
            FitToScreen();
        }
        InvalidateVisual();
    }

    private void ApplyResult(ImageLoadResult result, CancellationToken ct)
    {
        // P0 fix: don't dispose the current bitmap if the
        // fade-out animation is still drawing it. The previous
        // code unconditionally disposed _oldBitmap, which could
        // be the one being cross-faded in OnRender → crash. Now:
        // if _activeCrossFade is still alive, the old bitmap will
        // be disposed when the fade completes (see OnRender).
        // The new bitmap takes ownership of _bitmap; if no fade
        // is in flight, dispose the old one immediately.
        if (_activeCrossFade == null) _oldBitmap?.Dispose();
        _oldBitmap = _bitmap;
        _oldZoom = _zoom;
        _oldOffX = _offsetX;
        _oldOffY = _offsetY;
        _wbmp = null;
        _bitmap = null;

        if (result.IsSuccess && result.Bitmap != null)
        {
            _bitmap = result.Bitmap;
            _currentPath = result.FilePath;
            _sourceW = result.SourceWidth > 0 ? result.SourceWidth : result.Width;
            _sourceH = result.SourceHeight > 0 ? result.SourceHeight : result.Height;
            _dirty = true;
            _animOpacity = 0f;
            _rotation = 0;

            // Tell the world a new image is loaded. Subscribers can
            // copy Width/Height into bound models (ImageItem) so the
            // info pill and other displays get authoritative values
            // without needing to re-read the file header themselves.
            ImageLoaded?.Invoke(result);

            FitToScreen();
            DebugLog.Write("FS", $"ApplyResult: after fit src={result.SourceWidth}x{result.SourceHeight} bmp={_bitmap.Width}x{_bitmap.Height} zoom={_zoom:F4} fitScale={_fitScale:F4} viewport={ActualWidth:F0}x{ActualHeight:F0}");

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
        }
        else
        {
            DebugLog.Write("Viewer", $"load failed: {result.FilePath} → {result.ErrorMessage}");
            StatusChanged?.Invoke($"加载失败: {result.ErrorMessage}");
        }
        InvalidateVisual();
    }

    /// <summary>
    /// When true, the next FitToScreen call will snap the image to the
    /// fitted position immediately, with no zoom/offset animation.
    /// Used by the window to fullscreen transition so the image does
    /// not visibly slide from the old (windowed) viewer position to
    /// the new (fullscreen) one.
    /// </summary>
    public bool FitToScreenSkipAnimation { get; set; }

    /// <summary>
    /// Tell the viewer the underlying <c>SKBitmap</c>'s pixel data
    /// changed (e.g. after the editor applied a mosaic stroke
    /// directly to the source bitmap). Forces the cached
    /// <c>WriteableBitmap</c> to be re-uploaded on the next render
    /// pass. <see cref="InvalidateVisual"/> on its own is not enough
    /// because the <c>_dirty</c> flag short-circuits the re-upload
    /// when false. The editor calls this after each mosaic stroke so
    /// the next OnRender actually re-copies the modified source.
    /// </summary>
    public void NotifyContentChanged()
    {
        _dirty = true;
        InvalidateVisual();
    }

    /// <summary>
    /// True when the current zoom is within 1% of the auto-fit scale
    /// (i.e. the image is at "Fit to screen" size). Used by the host
    /// window's double-click handler to toggle Fit ↔ 100%.
    /// </summary>
    public bool IsAtFitScale => _bitmap != null && Math.Abs(_zoom - _fitScale) < 0.01f;

    /// <summary>
    /// Cancel any in-flight cross-fade animation and detach
    /// <see cref="CompositionTarget.Rendering"/> handlers. Called by
    /// the host window's <c>Closed</c> handler so the per-frame
    /// delegate (which captures this control via the lambda
    /// closure) is released before the window is GC'd. Without this
    /// hook a window closed mid-fade leaks until the process exits.
    /// </summary>
    public void AbortAnimations()
    {
        _upgradeTimer.Stop();
        var upg = _upgradeCts;
        if (upg != null) { upg.Cancel(); upg.Dispose(); _upgradeCts = null; }
        if (_activeCrossFade != null)
        {
            CompositionTarget.Rendering -= _activeCrossFade;
            _activeCrossFade = null;
        }
        if (_animating)
        {
            CompositionTarget.Rendering -= OnRendering;
            _animating = false;
        }
        _paintOld?.Dispose(); _paintOld = null;
        _paintNew?.Dispose(); _paintNew = null;
        _surface?.Dispose(); _surface = null;
    }

    public void FitToScreen()
    {
        DebugLog.Write("FS", $"FitToScreen: enter bitmap={( _bitmap == null ? "null" : $"{_bitmap.Width}x{_bitmap.Height}")} viewport={ActualWidth:F0}x{ActualHeight:F0} skip={FitToScreenSkipAnimation} zoom={_zoom:F4} fitScale={_fitScale:F4}");
        if (_bitmap == null)
        {
            return;
        }

        var w = (float)Math.Max(1, ActualWidth);
        var h = (float)Math.Max(1, ActualHeight);
        if (w <= 1 || h <= 1)
        {
            Dispatcher.BeginInvoke(() => FitToScreen());
            return;
        }

        float dispW = _bitmap.Width, dispH = _bitmap.Height;
        if (_rotation == 90 || _rotation == 270)
        {
            dispW = _bitmap.Height;
            dispH = _bitmap.Width;
        }
        _fitScale = Math.Min(w / dispW, h / dispH);
        _targetZoom = _fitScale;
        _targetOffsetX = (w - _bitmap.Width * _targetZoom) / 2f;
        _targetOffsetY = (h - _bitmap.Height * _targetZoom) / 2f;

        if (FitToScreenSkipAnimation)
        {
            // Snap to target without the zoom/offset animation. The
            // background cross-fade in the Window (AnimateViewerBackground)
            // is the only transition we run during window→fullscreen; the
            // image itself appears centred in the new bounds from frame 1.
            FitToScreenSkipAnimation = false;
            _animating = false;
            CompositionTarget.Rendering -= OnRendering;
            _zoom = _targetZoom;
            _offsetX = _targetOffsetX;
            _offsetY = _targetOffsetY;
            _dirty = true;
            InvalidateVisual();
            ZoomChanged?.Invoke(_targetZoom);
        }
        else
        {
            StartZoomAnim();
            ZoomChanged?.Invoke(_targetZoom);
        }
    }

    public void ZoomToOriginal()
    {
        if (_bitmap == null) return;
        _targetZoom = 1f;
        CenterImage();
        StartZoomAnim();
        ZoomChanged?.Invoke(_targetZoom);
    }

    public void RotateLeft()
    {
        if (_bitmap == null) return;
        Rotation = (_rotation - 90 + 360) % 360;
    }

    public void RotateRight()
    {
        if (_bitmap == null) return;
        Rotation = (_rotation + 90) % 360;
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

    /// <summary>
    /// Set the zoom factor without animation. Zooms anchored at
    /// the current viewport center (matching scroll-wheel behavior)
    /// so the image doesn't jump to center when the user drags
    /// the slider after panning.
    /// </summary>
    public void SetZoomImmediate(float zoom)
    {
        _targetZoom = Math.Clamp(zoom, 0.05f, 20f);
        if (_animating)
        {
            _animating = false;
            CompositionTarget.Rendering -= OnRendering;
        }
        // Zoom anchored at viewport center, like ZoomAtPoint
        // but targeting the middle of the view rather than the cursor.
        var cx = ActualWidth / 2d;
        var cy = ActualHeight / 2d;
        var worldX = (float)((cx - _offsetX) / _zoom);
        var worldY = (float)((cy - _offsetY) / _zoom);
        _zoom = _targetZoom;
        _targetOffsetX = (float)(cx - worldX * _targetZoom);
        _targetOffsetY = (float)(cy - worldY * _targetZoom);
        _offsetX = _targetOffsetX;
        _offsetY = _targetOffsetY;
        _dirty = true;
        InvalidateVisual();
        ZoomChanged?.Invoke(_zoom);
    }

    /// <summary>
    /// Force the viewer to re-render with the latest overlay. The
    /// overlay <see cref="SKBitmap"/> is mutated in-place (via
    /// <see cref="SKCanvas"/>); WPF has no way to know it changed,
    /// so an explicit dirty + invalidate is required. Without
    /// this, pen strokes only become visible after a zoom
    /// change forces a re-render.
    /// </summary>
    public void InvalidateOverlay()
    {
        _dirty = true;
        InvalidateVisual();
    }

    /// <summary>
    /// Zoom around a specific point (typically the mouse cursor).
    /// Used when an overlay intercepts mouse events and needs to
    /// forward wheel-zoom to the viewer with the correct origin.
    /// </summary>
    public void ZoomAtPoint(float wheelDelta, double x, double y)
    {
        if (_bitmap == null) return;
        var factor = wheelDelta > 0 ? 1.3f : 0.7f;
        _targetZoom = Math.Clamp(_zoom * factor, 0.05f, 20f);

        var worldX = (float)((x - _offsetX) / _zoom);
        var worldY = (float)((y - _offsetY) / _zoom);

        _targetOffsetX = (float)(x - worldX * _targetZoom);
        _targetOffsetY = (float)(y - worldY * _targetZoom);

        StartZoomAnim();
        ZoomChanged?.Invoke(_targetZoom);
    }

    /// <summary>
    /// Sample the average luminance of the currently-loaded image at
    /// the given viewport (render surface) coordinates. The viewport
    /// point is mapped back to bitmap pixels via the inverse of the
    /// render transform (offset → scale → rotate around center), so
    /// zoom, pan, and 90/180/270 rotation are all accounted for.
    /// Returns false when no bitmap is loaded or every sampled pixel
    /// falls outside the image (e.g. the point sits on the letterboxed
    /// border) — callers treat that as "no image content here".
    /// </summary>
    public bool TryGetLuminanceAt(double viewportX, double viewportY, double radius, out float luminance)
    {
        luminance = 0f;
        if (_bitmap == null) return false;

        var dx = (viewportX - _offsetX) / _zoom;
        var dy = (viewportY - _offsetY) / _zoom;

        double bx = dx, by = dy;
        if (_rotation != 0)
        {
            var cx = _bitmap.Width / 2f;
            var cy = _bitmap.Height / 2f;
            var qx = dx - cx;
            var qy = dy - cy;
            switch (_rotation)
            {
                case 90: bx = qy + cx; by = -qx + cy; break;
                case 180: bx = -qx + cx; by = -qy + cy; break;
                case 270: bx = -qy + cx; by = qx + cy; break;
            }
        }

        var r = (int)Math.Ceiling(radius);
        double sum = 0;
        var count = 0;
        for (var oy = -r; oy <= r; oy++)
        {
            for (var ox = -r; ox <= r; ox++)
            {
                var sx = (int)Math.Floor(bx + ox);
                var sy = (int)Math.Floor(by + oy);
                if (sx < 0 || sy < 0 || sx >= _bitmap.Width || sy >= _bitmap.Height) continue;
                var c = _bitmap.GetPixel(sx, sy);
                sum += 0.299 * c.Red + 0.587 * c.Green + 0.114 * c.Blue;
                count++;
            }
        }

        if (count == 0) return false;
        luminance = (float)(sum / count) / 255f;
        return true;
    }

    /// <summary>
    /// Load a pre-decoded <see cref="SKBitmap"/> directly into the
    /// viewer. Unlike <see cref="LoadImage"/>, no off-thread decode
    /// happens (the bitmap is already in memory). The viewer snaps
    /// to fit-to-screen without animation, so the caller does not
    /// see a slide-in transition.
    /// </summary>
    public void LoadBitmap(SKBitmap bitmap)
    {
        // Cancel any in-flight async load
        var oldCts = _loadCts;
        if (oldCts != null) { oldCts.Cancel(); oldCts.Dispose(); }
        _loadCts = null;

        // Stop any cross-fade in progress
        if (_activeCrossFade != null)
        {
            CompositionTarget.Rendering -= _activeCrossFade;
            _activeCrossFade = null;
        }

        // Dispose the previous image (no fade-out)
        _oldBitmap?.Dispose();
        _oldBitmap = null;

        _bitmap = bitmap;
        _currentPath = null;
        _sourceW = _sourceH = 0;
        _wbmp = null;
        _dirty = true;
        _rotation = 0;
        _overlayBitmap = null;

        // Snap to fit (no animation) so the editor opens at a
        // sensible zoom rather than zooming in from 0.
        FitToScreenSkipAnimation = true;
        FitToScreen();
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
            if (_rotation != 0)
            {
                canvas.Translate(_bitmap.Width / 2f, _bitmap.Height / 2f);
                canvas.RotateDegrees(_rotation);
                canvas.Translate(-_bitmap.Width / 2f, -_bitmap.Height / 2f);
            }
            canvas.DrawBitmap(_bitmap, 0, 0, _paintNew);
            canvas.Restore();
        }

        // Draw overlay (e.g. pen strokes) on top of the new bitmap
        // using the same transform. Only visible while the cross-fade
        // is complete (_animOpacity ~ 1); during a transition the
        // viewer is still loading a new image so the overlay is hidden
        // to avoid showing it against the wrong source.
        if (_overlayBitmap != null && _bitmap != null && _animOpacity > 0.99f)
        {
            canvas.Save();
            canvas.Translate(_offsetX, _offsetY);
            canvas.Scale(_zoom);
            if (_rotation != 0)
            {
                canvas.Translate(_bitmap.Width / 2f, _bitmap.Height / 2f);
                canvas.RotateDegrees(_rotation);
                canvas.Translate(-_bitmap.Width / 2f, -_bitmap.Height / 2f);
            }
            canvas.DrawBitmap(_overlayBitmap, 0, 0, _paintNew);
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
        // Drop reentrant calls from screenshot tools. The first call
        // continues; the second call (which can happen if the print
        // path forces WPF to re-evaluate the visual tree while our
        // backbuffer is being written) returns immediately and the
        // screenshot just captures the previous frame.
        if (_inRender) return;
        if (_bitmap == null) return;

        _inRender = true;
        try
        {
            if (_dirty || _wbmp == null)
                RenderToWriteableBitmap();

            if (_wbmp != null)
                dc.DrawImage(_wbmp, new Rect(RenderSize));
        }
        finally
        {
            _inRender = false;
        }
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        DebugLog.Write("FS", $"OnRenderSizeChanged: {sizeInfo.PreviousSize.Width:F0}x{sizeInfo.PreviousSize.Height:F0} -> {sizeInfo.NewSize.Width:F0}x{sizeInfo.NewSize.Height:F0}");
        // Force WriteableBitmap to be re-allocated to new size, otherwise
        // it will reuse stale dimensions and clip incorrectly.
        _wbmp = null;
        _dirty = true;
        InvalidateVisual();
        HandleViewportResized();
    }

    /// <summary>
    /// Viewport resize policy: a resize only re-fits an image that
    /// is currently fitted (<see cref="IsAtFitScale"/>). A
    /// deliberately zoomed/paned image keeps the user's zoom — a
    /// window resize must not yank it back to fit. If the resize
    /// arrives while a fit animation or a drag is in flight, the
    /// refit is deferred via <see cref="_pendingRefit"/> and
    /// executed when the interaction ends (never dropped).
    /// </summary>
    private void HandleViewportResized()
    {
        DebugLog.Write("FS", $"HandleViewportResized: bitmap={( _bitmap == null ? "null" : $"{_bitmap.Width}x{_bitmap.Height}")} zoom={_zoom:F4} fitScale={_fitScale:F4} atFit={IsAtFitScale} drag={_isDragging} anim={_animating} pending={_pendingRefit}");
        if (_bitmap == null) return;
        if (_isDragging || _animating)
        {
            _pendingRefit = true;
            return;
        }
        if (IsAtFitScale)
            FitToScreen();
    }

    /// <summary>
    /// Run the deferred refit once the interaction that blocked it
    /// (fit animation, drag) has ended. The <see cref="IsAtFitScale"/>
    /// re-check makes the policy consistent with
    /// <see cref="HandleViewportResized"/>: a resize during a
    /// user-initiated scroll-zoom (target ≠ fit scale) does not
    /// refit; a resize during a fit animation does.
    /// </summary>
    private void FinishPendingRefit()
    {
        DebugLog.Write("FS", $"FinishPendingRefit: pending={_pendingRefit} bitmap={( _bitmap == null ? "null" : $"{_bitmap.Width}x{_bitmap.Height}")} zoom={_zoom:F4} fitScale={_fitScale:F4} drag={_isDragging} anim={_animating}");
        if (!_pendingRefit) return;
        _pendingRefit = false;
        if (_bitmap == null || _isDragging || _animating) return;
        if (IsAtFitScale)
            FitToScreen();
    }

    /// <summary>
    /// Mouse capture can be lost without a MouseLeftButtonUp when
    /// focus leaves the window mid-pan (Alt-Tab, modal dialog,
    /// capture stolen). Without this the <see cref="_isDragging"/>
    /// gate stays stuck and every later resize-refit is skipped
    /// forever. Closing the gate here keeps the interaction state
    /// machine self-consistent.
    /// </summary>
    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (!_isDragging) return;
        _isDragging = false;
        _isPanning = false;
        FinishPendingRefit();
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
        FinishPendingRefit();

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