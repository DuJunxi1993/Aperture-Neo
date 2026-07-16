using System;
using System.Collections.Generic;
using SkiaSharp;

namespace ApertureNeo.Controls.Annotation;

/// <summary>
/// Shared annotation state for both the main app's image viewer
/// (when in annotation mode) and the screenshot editor. Owns the
/// list of committed pen strokes and the <see cref="SKBitmap"/>
/// that the viewer composites on top of the source image.
///
/// 保留以供将来开发：主程序 view 栏的标注模式当前已被
/// EditorWindow 弹出编辑替代，此文件和关联的 AnnotationOverlay、
/// PenCanvasBehavior 均处于闲置状态（IsAnnotating 永远为 false）。
///
/// Supports two drawing modes:
/// <list type="bullet">
///   <item>Pen (color) mode — committed list of stroke paths</item>
///   <item>Mosaic mode — each commit captures the pre-mosaic pixel
///         data into a history entry so Undo can restore the original
///         pixels exactly.</item>
/// </list>
///
/// Lifecycle:
///   - Construct with the image dimensions (the OverlayBitmap is
///     allocated to that size).
///   - PenCanvas / AnnotationOverlay push strokes via
///     <see cref="BeginStroke"/>, <see cref="ExtendStroke"/>,
///     <see cref="CommitStroke"/>, and read them via
///     <see cref="CommittedStrokes"/>.
///   - Mosaic uses <see cref="BeginMosaic"/> / <see cref="ApplyMosaicRectangle"/>
///     / <see cref="CommitMosaic"/>.
///   - After every mutation, <see cref="RedrawRequested"/> fires
///     so the host (SkiaImageViewer) calls
///     <c>SkiaImageViewer.InvalidateOverlay()</c>.
///
/// Threading: all members are UI-thread-only (same as the
/// SkiaImageViewer that hosts the OverlayBitmap).
/// </summary>
public sealed class AnnotationState
{
    /// <summary>Bitmap composited on top of the source image. The
    /// viewer reads this through its <c>OverlayBitmap</c> property
    /// and re-renders on <c>RedrawRequested</c>.</summary>
    public SKBitmap OverlayBitmap { get; }

    /// <summary>Color and stroke width of the next stroke. Set
    /// by the color / size pickers in the annotation toolbar.</summary>
    public SKColor CurrentColor { get; set; } = SKColors.Red;
    public float CurrentSize { get; set; } = 4f;

    /// <summary>Host-supplied DPI scale factor (1.0 at 96 DPI,
    /// 2.0 at 192 DPI, etc.). Used to scale the raw
    /// <see cref="CurrentSize"/> value (which the user types in
    /// as if it were screen pixels) up to actual device pixels
    /// when rendering strokes and mosaic blocks. Without this
    /// multiplier, "8" on a 192-DPI display renders the same 8
    /// image-pixel stroke as on a 96-DPI display — which the
    /// user perceives as "much thinner" on the higher-DPI
    /// screen. Default 1.0 keeps the historical behaviour for
    /// any host that doesn't set it.</summary>
    public float DpiScale { get; set; } = 1.0f;

    /// <summary>Optional reference to the source image bitmap.
    /// When set, <see cref="ApplyMosaicRectangle"/> reads source
    /// pixels for averaging instead of the overlay (which is
    /// transparent). Used by the main viewer's annotation mode;
    /// the screenshot editor has its own mosaic pipeline and
    /// leaves this null.</summary>
    public SKBitmap? SourceBitmap { get; set; }

    /// <summary>True after the user has drawn strokes that
    /// haven't been saved to disk. Reset to false by
    /// <c>AnnotationSaveService</c> on successful save.</summary>
    public bool HasUnsavedChanges { get; set; }

    /// <summary>Read-only view of the committed pen strokes. The
    /// in-progress stroke is held internally and not exposed
    /// here until it commits.</summary>
    public IReadOnlyList<StrokeData> CommittedStrokes => _committed;

    /// <summary>Read-only view of the committed mosaic rectangles
    /// (newest last). Undo pops from this list and restores the
    /// original pixels from the corresponding history entry.</summary>
    public IReadOnlyList<MosaicData> CommittedMosaics => _mosaicHistory;

    /// <summary>Raised after any mutation that requires the
    /// OverlayBitmap to be re-uploaded to the viewer. Subscribers
    /// should call <c>SkiaImageViewer.InvalidateOverlay()</c>.</summary>
    public event EventHandler? RedrawRequested;

    private readonly List<StrokeData> _committed = new();
    private List<SKPoint> _inProgress = new();
    private SKColor _activeColor;
    private float _activeSize;

    // Mosaic state. _mosaicHistory holds completed mosaic operations
    // (newest last); Undo pops the last entry and restores the
    // original pixels from OriginalData. _mosaicOriginalData is a
    // scratch buffer for the in-progress drag-rectangle's pixel
    // backup — moved to the history on CommitMosaic.
    // _mosaicActiveRect is the USER's drag rect; _mosaicAlignedRect
    // is the block-aligned version that's actually mosaiced (can be
    // up to blockSize pixels wider/taller on each side). The
    // history stores the ALIGNED rect so Undo restores over the
    // exact bounds that were mosaiced.
    private readonly List<MosaicData> _mosaicHistory = new();
    private SKRectI? _mosaicActiveRect;
    private SKRectI? _mosaicAlignedRect;
    private byte[]? _mosaicOriginalData;
    private byte[]? _mosaicCurrentPixels;

    public AnnotationState(int width, int height)
    {
        OverlayBitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(OverlayBitmap);
        canvas.Clear(SKColors.Transparent);
    }

    /// <summary>Start a new stroke. Captures the current
    /// color/size so subsequent <see cref="ExtendStroke"/>
    /// points are tagged with the same pen settings even if the
    /// user changes them mid-stroke. The size is multiplied by
    /// <see cref="DpiScale"/> so a "20" brush on a 192-DPI
    /// display renders as a 40-image-pixel stroke — matching
    /// what the user sees on screen.</summary>
    public void BeginStroke()
    {
        _inProgress = new List<SKPoint>();
        _activeColor = CurrentColor;
        _activeSize = CurrentSize * DpiScale;
    }

    /// <summary>Append a point to the in-progress stroke and
    /// trigger a redraw that includes the partial stroke so the
    /// user sees their pen trail in real time.</summary>
    public void ExtendStroke(SKPoint point)
    {
        _inProgress.Add(point);
        RedrawOverlay();
        RedrawRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Finalize the in-progress stroke. Strokes with
    /// fewer than 2 points are discarded (clicks without drag).</summary>
    public void CommitStroke()
    {
        if (_inProgress.Count >= 2)
        {
            _committed.Add(new StrokeData(new List<SKPoint>(_inProgress), _activeColor, _activeSize));
            HasUnsavedChanges = true;
        }
        _inProgress = new List<SKPoint>();
        RedrawOverlay();
        RedrawRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Drop the last committed pen stroke (if any) or
    /// the last mosaic rectangle (if any). Mosaic rectangles
    /// have priority over pen strokes because they're typically
    /// the larger commitment. Returns true if anything was
    /// undone, false if both lists were empty.</summary>
    public bool Undo()
    {
        if (_mosaicHistory.Count > 0)
        {
            var entry = _mosaicHistory[^1];
            _mosaicHistory.RemoveAt(_mosaicHistory.Count - 1);
            RestoreMosaic(entry);
            HasUnsavedChanges = true;
            RedrawOverlay();
            RedrawRequested?.Invoke(this, EventArgs.Empty);
            return true;
        }
        if (_committed.Count > 0)
        {
            _committed.RemoveAt(_committed.Count - 1);
            HasUnsavedChanges = true;
            RedrawOverlay();
            RedrawRequested?.Invoke(this, EventArgs.Empty);
            return true;
        }
        return false;
    }

    /// <summary>Drop all committed strokes and the in-progress
    /// stroke. <see cref="HasUnsavedChanges"/> is reset to false
    /// (after a clear the editor is "clean" — the user explicitly
    /// asked for an empty state).</summary>
    public void Clear()
    {
        if (_committed.Count == 0
            && _inProgress.Count == 0
            && _mosaicHistory.Count == 0) return;
        _committed.Clear();
        _inProgress = new List<SKPoint>();
        _mosaicAlignedRect = null;
        _mosaicHistory.Clear();
        _mosaicActiveRect = null;
        _mosaicOriginalData = null;
        _mosaicCurrentPixels = null;
        HasUnsavedChanges = false;
        RedrawOverlay();
        RedrawRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Begin a mosaic drag. Caller passes the user's
    /// drag rectangle (image-space). We stash it so
    /// <see cref="ApplyMosaicRectangle"/> can compute the
    /// aligned rect. Pixel backup is deferred to ApplyMosaic
    /// because the actual mosaic region (after block-alignment)
    /// can be up to <c>blockSize</c> pixels wider/taller on each
    /// side than the drag rect — backing up at the drag-rect size
    /// here used to cause <see cref="ApplyMosaicAverage"/> to
    /// read past the end of the backup buffer (IndexOutOfRangeException
    /// was the symptom; the EditorWindow's parallel flow
    /// already does it the correct way).</summary>
    public void BeginMosaic(SKRectI rect)
    {
        if (rect.Width <= 0 || rect.Height <= 0) return;
        _mosaicActiveRect = rect;
        _mosaicAlignedRect = null;
        _mosaicOriginalData = null;
    }

    /// <summary>Block-align a rectangle to the mosaic grid.
    /// Returns a rect snapped outward to the nearest blockSize
    /// boundaries, clamped to (0, 0, width, height).</summary>
    public static SKRectI AlignRect(SKRectI rect, int blockSize, int maxW, int maxH)
    {
        int x0 = (rect.Left / blockSize) * blockSize;
        int y0 = (rect.Top / blockSize) * blockSize;
        int x1 = ((rect.Right + blockSize) / blockSize) * blockSize;
        int y1 = ((rect.Bottom + blockSize) / blockSize) * blockSize;
        x0 = Math.Max(0, x0);
        y0 = Math.Max(0, y0);
        x1 = Math.Min(maxW, x1);
        y1 = Math.Min(maxH, y1);
        return new SKRectI(x0, y0, x1, y1);
    }

    /// <summary>Apply the per-block-average mosaic effect to the
    /// active rectangle. Pixel data is read from a fresh backup
    /// taken at the ALIGNED rect (so the average reflects the
    /// pre-mosaic state — repeated mosaic strokes don't compound
    /// blur). <see cref="CurrentSize"/> is multiplied by
    /// <see cref="DpiScale"/> so the block size matches the
    /// visible pen size on hi-DPI screens.</summary>
    public void ApplyMosaicRectangle()
    {
        if (_mosaicActiveRect is not { } userRect) return;
        int blockSize = Math.Max(2, (int)Math.Round(CurrentSize * DpiScale));

        // Block-align the user's drag rect to the mosaic grid
        // so consecutive strokes align with each other. The
        // aligned rect can grow by up to blockSize pixels on
        // every side relative to the drag rect.
        var aligned = AlignRect(userRect, blockSize, OverlayBitmap.Width, OverlayBitmap.Height);
        if (aligned.Width <= 0 || aligned.Height <= 0) return;

        // Backup the overlay pixels at the ALIGNED size for undo.
        _mosaicAlignedRect = aligned;
        _mosaicOriginalData = BackupMosaicPixels(aligned);

        if (SourceBitmap != null)
        {
            // Annotation mode: read source image pixels for the
            // block average so mosaic blocks reflect the actual
            // image content (not the transparent overlay).
            ApplySourceMosaicAverage(aligned, blockSize);
        }
        else
        {
            // Fallback: average overlay pixels (legacy / editor
            // path that has its own source-level mosaic).
            ApplyMosaicAverage(aligned, _mosaicOriginalData, blockSize);
        }
        // Backup the mosaic result so RedrawOverlay can restore
        // it after clearing (otherwise pen strokes would wipe it).
        _mosaicCurrentPixels = BackupMosaicPixels(aligned);
        RedrawRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Finalize the active mosaic. The history entry
    /// stores the ALIGNED rect + its pre-mosaic pixel data so
    /// Undo can restore the exact pre-mosaic area. Returns true
    /// if a mosaic was committed, false if there was nothing to
    /// commit.</summary>
    public bool CommitMosaic()
    {
        if (_mosaicActiveRect is null
            || _mosaicAlignedRect is not { } aligned
            || _mosaicOriginalData is null) return false;
        // Store the ALIGNED rect — that's the region we
        // actually mosaiced, so Undo needs to restore over the
        // exact same bounds. Also store the mosaic result pixels
        // so RedrawOverlay can restore them.
        _mosaicHistory.Add(new MosaicData(aligned, _mosaicOriginalData, _mosaicCurrentPixels));
        _mosaicActiveRect = null;
        _mosaicAlignedRect = null;
        _mosaicOriginalData = null;
        _mosaicCurrentPixels = null;
        HasUnsavedChanges = true;
        return true;
    }

    private byte[] BackupMosaicPixels(SKRectI rect)
    {
        int w = rect.Width;
        int h = rect.Height;
        int stride = w * 4;
        var data = new byte[stride * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                var c = OverlayBitmap.GetPixel(rect.Left + x, rect.Top + y);
                int idx = y * stride + x * 4;
                data[idx + 0] = c.Blue;
                data[idx + 1] = c.Green;
                data[idx + 2] = c.Red;
                data[idx + 3] = c.Alpha;
            }
        }
        return data;
    }

    private void ApplyMosaicAverage(SKRectI rect, byte[] original, int blockSize)
    {
        int w = rect.Width;
        int h = rect.Height;
        int stride = w * 4;

        for (int by = 0; by < h; by += blockSize)
        {
            int bh = Math.Min(blockSize, h - by);
            for (int bx = 0; bx < w; bx += blockSize)
            {
                int bw = Math.Min(blockSize, w - bx);
                long sumB = 0, sumG = 0, sumR = 0, sumA = 0;
                int count = 0;
                for (int py = 0; py < bh; py++)
                {
                    for (int px = 0; px < bw; px++)
                    {
                        int idx = (by + py) * stride + (bx + px) * 4;
                        sumB += original[idx + 0];
                        sumG += original[idx + 1];
                        sumR += original[idx + 2];
                        sumA += original[idx + 3];
                        count++;
                    }
                }
                var avg = new SKColor(
                    (byte)(sumR / count), (byte)(sumG / count),
                    (byte)(sumB / count), (byte)(sumA / count));
                for (int py = 0; py < bh; py++)
                {
                    for (int px = 0; px < bw; px++)
                    {
                        OverlayBitmap.SetPixel(rect.Left + bx + px, rect.Top + by + py, avg);
                    }
                }
            }
        }
    }

    /// <summary>Mosaic block-average computed from source image
    /// pixels. Used by the annotation mode where
    /// <see cref="SourceBitmap"/> is set. Reads original image
    /// pixels, averages per block, and writes opaque blocks to
    /// the overlay so the viewer composites the mosaic on top of
    /// the source image.</summary>
    private void ApplySourceMosaicAverage(SKRectI rect, int blockSize)
    {
        if (SourceBitmap == null) return;
        int w = rect.Width;
        int h = rect.Height;

        for (int by = 0; by < h; by += blockSize)
        {
            int bh = Math.Min(blockSize, h - by);
            for (int bx = 0; bx < w; bx += blockSize)
            {
                int bw = Math.Min(blockSize, w - bx);
                long sumB = 0, sumG = 0, sumR = 0, sumA = 0;
                int count = 0;
                for (int py = 0; py < bh; py++)
                {
                    for (int px = 0; px < bw; px++)
                    {
                        var c = SourceBitmap.GetPixel(rect.Left + bx + px, rect.Top + by + py);
                        sumB += c.Blue;
                        sumG += c.Green;
                        sumR += c.Red;
                        sumA += c.Alpha;
                        count++;
                    }
                }
                var avg = new SKColor(
                    (byte)(sumR / count), (byte)(sumG / count),
                    (byte)(sumB / count), (byte)(sumA / count));
                for (int py = 0; py < bh; py++)
                {
                    for (int px = 0; px < bw; px++)
                    {
                        OverlayBitmap.SetPixel(rect.Left + bx + px, rect.Top + by + py, avg);
                    }
                }
            }
        }
    }

    private void RestoreMosaic(MosaicData entry)
    {
        int w = entry.Rect.Width;
        int h = entry.Rect.Height;
        int stride = w * 4;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int idx = y * stride + x * 4;
                var c = new SKColor(
                    entry.OriginalData[idx + 2], entry.OriginalData[idx + 1],
                    entry.OriginalData[idx + 0], entry.OriginalData[idx + 3]);
                OverlayBitmap.SetPixel(entry.Rect.Left + x, entry.Rect.Top + y, c);
            }
        }
    }

    private void RestoreMosaicPixels(SKRectI rect, byte[] pixels)
    {
        int w = rect.Width;
        int h = rect.Height;
        int stride = w * 4;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int idx = y * stride + x * 4;
                var c = new SKColor(
                    pixels[idx + 2], pixels[idx + 1],
                    pixels[idx + 0], pixels[idx + 3]);
                OverlayBitmap.SetPixel(rect.Left + x, rect.Top + y, c);
            }
        }
    }

    private void RedrawOverlay()
    {
        using var canvas = new SKCanvas(OverlayBitmap);
        canvas.Clear(SKColors.Transparent);
        foreach (var stroke in _committed)
        {
            DrawStroke(canvas, stroke);
        }
        if (_inProgress.Count >= 2)
        {
            DrawStroke(canvas, new StrokeData(_inProgress, _activeColor, _activeSize));
        }
        // Restore committed mosaic rectangles (which were written
        // as opaque blocks to the overlay by ApplyMosaicRectangle).
        foreach (var m in _mosaicHistory)
        {
            if (m.MosaicPixels != null)
                RestoreMosaicPixels(m.Rect, m.MosaicPixels);
        }
    }

    private static void DrawStroke(SKCanvas canvas, StrokeData stroke)
    {
        if (stroke.Points.Count < 2) return;
        using var paint = new SKPaint
        {
            Color = stroke.Color,
            StrokeWidth = stroke.Size,
            Style = SKPaintStyle.Stroke,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
            IsAntialias = true,
        };
        using var path = new SKPath();
        path.MoveTo(stroke.Points[0]);
        for (int i = 1; i < stroke.Points.Count; i++)
        {
            path.LineTo(stroke.Points[i]);
        }
        canvas.DrawPath(path, paint);
    }

    public sealed record StrokeData(List<SKPoint> Points, SKColor Color, float Size);

    /// <summary>One committed mosaic rectangle. Stores the
    /// pre-mosaic pixel data so Undo can restore the area
    /// exactly, and the mosaic result pixels so
    /// <see cref="RedrawOverlay"/> can restore the overlay
    /// without re-computing the average. <see cref="Rect"/>
    /// is in image-space pixel coordinates (same as the host
    /// image bounds).</summary>
    public sealed record MosaicData(SKRectI Rect, byte[] OriginalData, byte[]? MosaicPixels);
}