using System;
using System.Windows;
using System.Windows.Input;
using SkiaSharp;
using ApertureNeo.Controls.Annotation;

namespace ApertureNeo.Views.Shared;

/// <summary>
/// Shared PenCanvas mouse behaviour for both the main app's image
/// viewer (when in annotation mode) and the screenshot editor.
/// Encapsulates the "click + drag = draw, release = commit" flow
/// and routes mouse events to the right code path based on the
/// current <see cref="AnnotationMode"/>.
///
/// 保留以供将来开发：主程序 view 栏的标注模式当前已被
/// EditorWindow 弹出编辑替代，此文件处于闲置状态。
///
/// Why a class (not a behavior or attached property): the
/// EditorWindow's existing mouse handlers already work — this
/// class is the extracted, reusable form that the main viewer
/// binds to. Both call sites follow the same pattern:
/// <list type="number">
///   <item>Subscribe the three mouse events on the PenCanvas.</item>
///   <item>Each handler calls into <see cref="OnMouseDown/Move/Up"/>
///         with the screen-space point and the host's image-coord
///         transform.</item>
///   <item>The behaviour routes to <see cref="AnnotationState"/>
///         (color or mosaic mode) and toggles a preview rectangle
///         for the live mosaic drag.</item>
/// </list>
///
/// Image-coordinate math: the host supplies a
/// <see cref="ToImageCoords"/> delegate that converts a screen
/// point to image-space (taking zoom + pan into account). When
/// the user drags outside the image bounds, the delegate returns
/// null and the behaviour ignores the event.
/// </summary>
public sealed class PenCanvasBehavior
{
    public enum AnnotationMode { Color, Mosaic }

    private readonly AnnotationState _state;
    private readonly FrameworkElement _previewRect;
    private readonly ToImageCoord _toImage;

    // Color mode scratch state.
    private bool _isPen;

    // Mosaic mode scratch state.
    private SKPoint _mosaicStart;
    private SKPoint _mosaicCurrent;
    private bool _isDraggingMosaic;

    public delegate SKPoint? ToImageCoord(Point screen);

    public PenCanvasBehavior(
        AnnotationState state,
        FrameworkElement mosaicPreviewRect,
        ToImageCoord toImage)
    {
        _state = state;
        _previewRect = mosaicPreviewRect;
        _toImage = toImage;
    }

    public AnnotationMode Mode { get; set; } = AnnotationMode.Color;

    /// <summary>True while the user is in the middle of a stroke
    /// or mosaic drag. Host checks this in KeyDown / Closing to
    /// decide whether to allow safe exits.</summary>
    public bool IsDrawing => _isPen || _isDraggingMosaic;

    public void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var pt = _toImage(e.GetPosition((IInputElement)sender));
        if (pt == null) return;

        if (Mode == AnnotationMode.Mosaic)
        {
            _mosaicStart = pt.Value;
            _mosaicCurrent = pt.Value;
            _isDraggingMosaic = true;
            _isPen = false;
            _previewRect.Visibility = Visibility.Visible;
            UpdateMosaicPreview();
        }
        else
        {
            _isPen = true;
            _state.BeginStroke();
            _state.ExtendStroke(pt.Value);
        }
        ((IInputElement)sender).CaptureMouse();
    }

    public void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_isDraggingMosaic)
        {
            var pt = _toImage(e.GetPosition((IInputElement)sender));
            if (pt == null) return;
            _mosaicCurrent = pt.Value;
            UpdateMosaicPreview();
        }
        else if (_isPen)
        {
            if (e.LeftButton != MouseButtonState.Pressed) return;
            var pt = _toImage(e.GetPosition((IInputElement)sender));
            if (pt == null) return;
            _state.ExtendStroke(pt.Value);
        }
    }

    public void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDraggingMosaic)
        {
            _isDraggingMosaic = false;
            ((IInputElement)sender).ReleaseMouseCapture();
            CommitMosaicDrag();
            _previewRect.Visibility = Visibility.Collapsed;
        }
        else if (_isPen)
        {
            _isPen = false;
            ((IInputElement)sender).ReleaseMouseCapture();
            _state.CommitStroke();
        }
    }

    /// <summary>Cancels any in-progress stroke/mosaic without
    /// committing. Used when the host's LostMouseCapture fires
    /// (e.g. Alt+Tab mid-drag) so the behaviour doesn't get
    /// stuck with a dangling preview rect.</summary>
    public void Cancel()
    {
        if (_isDraggingMosaic)
        {
            _isDraggingMosaic = false;
            _previewRect.Visibility = Visibility.Collapsed;
        }
        _isPen = false;
    }

    private void CommitMosaicDrag()
    {
        var rect = MakeMosaicRect(_mosaicStart, _mosaicCurrent);
        if (rect.Width <= 0 || rect.Height <= 0) return;
        _state.BeginMosaic(rect);
        _state.ApplyMosaicRectangle();
        _state.CommitMosaic();
    }

    private void UpdateMosaicPreview()
    {
        var r = GetAlignedRect(_mosaicStart, _mosaicCurrent);
        // The host is responsible for positioning the preview rect
        // (it has the image-to-screen transform). We just set size
        // here; the host reads CurrentMosaicRect and re-positions.
        _previewRect.Width = r.Width;
        _previewRect.Height = r.Height;
    }

    /// <summary>Current image-space mosaic rect (during drag).
    /// Returns <c>null</c> when not dragging. Host reads this to
    /// position the preview rectangle on screen via
    /// <c>Canvas.SetLeft/SetTop</c>.</summary>
    public SKRectI? CurrentMosaicRect =>
        _isDraggingMosaic ? GetAlignedRect(_mosaicStart, _mosaicCurrent) : null;

    /// <summary>Block-aligned mosaic rect matching what
    /// <c>AnnotationState.ApplyMosaicRectangle</c> will actually
    /// pixelate. Preview matches effect.</summary>
    private SKRectI GetAlignedRect(SKPoint a, SKPoint b)
    {
        int blockSize = Math.Max(2, (int)Math.Round(_state.CurrentSize * _state.DpiScale));
        var raw = MakeMosaicRect(a, b);
        return AnnotationState.AlignRect(raw, blockSize, _state.OverlayBitmap.Width, _state.OverlayBitmap.Height);
    }

    private static SKRectI MakeMosaicRect(SKPoint a, SKPoint b)
    {
        int x1 = (int)Math.Round(Math.Min(a.X, b.X));
        int y1 = (int)Math.Round(Math.Min(a.Y, b.Y));
        int x2 = (int)Math.Round(Math.Max(a.X, b.X));
        int y2 = (int)Math.Round(Math.Max(a.Y, b.Y));
        return new SKRectI(x1, y1, x2, y2);
    }
}