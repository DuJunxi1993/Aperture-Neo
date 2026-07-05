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
/// Lifecycle:
///   - Construct with the image dimensions (the OverlayBitmap is
///     allocated to that size).
///   - PenCanvas / AnnotationOverlay push strokes via
///     <see cref="BeginStroke"/>, <see cref="ExtendStroke"/>,
///     <see cref="CommitStroke"/>, and read them via
///     <see cref="CommittedStrokes"/>.
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
    /// and re-renders on <see cref="RedrawRequested"/>.</summary>
    public SKBitmap OverlayBitmap { get; }

    /// <summary>Color and stroke width of the next stroke. Set
    /// by the color / size pickers in the annotation toolbar.</summary>
    public SKColor CurrentColor { get; set; } = SKColors.Red;
    public float CurrentSize { get; set; } = 4f;

    /// <summary>True after the user has drawn strokes that
    /// haven't been saved to disk. Reset to false by
    /// <c>AnnotationSaveService</c> on successful save.</summary>
    public bool HasUnsavedChanges { get; set; }

    /// <summary>Read-only view of the committed strokes. The
    /// in-progress stroke is held internally and not exposed
    /// here until it commits.</summary>
    public IReadOnlyList<StrokeData> CommittedStrokes => _committed;

    /// <summary>Raised after any mutation that requires the
    /// OverlayBitmap to be re-uploaded to the viewer. Subscribers
    /// should call <c>SkiaImageViewer.InvalidateOverlay()</c>.</summary>
    public event EventHandler? RedrawRequested;

    private readonly List<StrokeData> _committed = new();
    private List<SKPoint> _inProgress = new();
    private SKColor _activeColor;
    private float _activeSize;

    public AnnotationState(int width, int height)
    {
        OverlayBitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(OverlayBitmap);
        canvas.Clear(SKColors.Transparent);
    }

    /// <summary>Start a new stroke. Captures the current
    /// color/size so subsequent <see cref="ExtendStroke"/>
    /// points are tagged with the same pen settings even if the
    /// user changes them mid-stroke.</summary>
    public void BeginStroke()
    {
        _inProgress = new List<SKPoint>();
        _activeColor = CurrentColor;
        _activeSize = CurrentSize;
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

    /// <summary>Drop the last committed stroke and redraw.</summary>
    public void Undo()
    {
        if (_committed.Count == 0) return;
        _committed.RemoveAt(_committed.Count - 1);
        RedrawOverlay();
        RedrawRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Drop all committed strokes and the in-progress
    /// stroke. <see cref="HasUnsavedChanges"/> is reset to false
    /// (after a clear the editor is "clean" — the user explicitly
    /// asked for an empty state).</summary>
    public void Clear()
    {
        if (_committed.Count == 0 && _inProgress.Count == 0) return;
        _committed.Clear();
        _inProgress = new List<SKPoint>();
        HasUnsavedChanges = false;
        RedrawOverlay();
        RedrawRequested?.Invoke(this, EventArgs.Empty);
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
}
