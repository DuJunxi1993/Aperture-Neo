using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SkiaSharp;

namespace ApertureNeo.Plugins.Screenshot;

/// <summary>
/// Annotation editor for a captured screenshot. Uses
/// <see cref="SkiaImageViewer"/> for image display and zoom/pan,
/// and a transparent <see cref="PenCanvas"/> overlaid on top for
/// pen strokes. Strokes are accumulated into a single
/// <see cref="SKBitmap"/> overlay that the viewer composites on
/// top of the source image at render time.
///
/// Performance notes: every pen stroke point triggers an overlay
/// redraw (clear + replay all committed strokes + current
/// in-progress stroke). The redraw is a single SkiaSharp canvas
/// operation, so it stays smooth for typical stroke counts. The
/// SkiaImageViewer then renders the composite at the viewer's
/// own rate (driven by WPF's render path), so the per-mouse-move
/// redraw is decoupled from the on-screen frame rate.
/// </summary>
public partial class EditorWindow : Window
{
    private readonly Bitmap _originalBitmap;
    private readonly SKBitmap _originalSkBitmap;
    private readonly SKBitmap _overlaySkBitmap;
    private readonly int _width, _height;

    private bool _isDrawing;
    private List<SKPoint> _currentStroke = new();
    private readonly List<StrokeData> _committedStrokes = new();
    private int _strokeCount;

    private SKColor _currentColor = SKColors.Red;
    private float _currentSize = 4f;

    // Guard against re-entrant slider/zoom updates. When the
    // SkiaViewer's ZoomChanged handler writes ZoomSlider.Value,
    // that fires ValueChanged which would otherwise call back
    // into SetZoomImmediate. The flag breaks the cycle.
    private bool _suspendSliderUpdate;

    private record StrokeData(List<SKPoint> Points, SKColor Color, float Size);

    public EditorWindow(Bitmap capturedBitmap)
    {
        InitializeComponent();

        // Set the three resource-backed visual properties in code
        // rather than as Window XAML attributes. The {StaticResource}
        // lookups on the <Window> opening tag are evaluated by the
        // XAML parser *before* <Window.Resources> is processed, so
        // they fail at parse time even though the resources exist
        // a few lines below. Resolving them via FindResource after
        // InitializeComponent runs the lookup at a time when
        // Window.Resources is fully loaded.
        Background = (System.Windows.Media.Brush)FindResource("SurfaceCanvas");
        Foreground = (System.Windows.Media.Brush)FindResource("TextPrimary");
        FontFamily = (System.Windows.Media.FontFamily)FindResource("FontPrimary");

        _originalBitmap = capturedBitmap;
        _width = capturedBitmap.Width;
        _height = capturedBitmap.Height;

        // Convert the GDI+ bitmap once into an SKBitmap that
        // the SkiaImageViewer can render directly. The GDI+
        // bitmap stays alive for BuildFinalImage (Save/Copy/OCR
        // all need a GDI+ Bitmap for the existing helpers).
        _originalSkBitmap = ToSkBitmap(capturedBitmap);

        // Overlay SKBitmap: same size as the source, fully
        // transparent. Strokes are drawn onto this canvas and
        // the viewer composites it on top of the source.
        _overlaySkBitmap = new SKBitmap(_width, _height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(_overlaySkBitmap))
        {
            canvas.Clear(SKColors.Transparent);
        }

        // Defensive: if capture is lost (Alt+Tab, etc.) the
        // drawing state machine must reset. Without this, a
        // half-drawn stroke could leave _isDrawing stuck on.
        PenCanvas.LostMouseCapture += (_, _) => { _isDrawing = false; };
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // Hand the bitmaps to the SkiaImageViewer. The viewer
        // calls FitToScreen inside LoadBitmap (no animation), so
        // the editor opens already framed correctly.
        SkiaViewer.LoadBitmap(_originalSkBitmap);
        SkiaViewer.OverlayBitmap = _overlaySkBitmap;

        // Sync UI to the viewer's state. ZoomChanged fires on
        // any zoom change; we route it back to the slider + label
        // with a re-entrancy guard so the slider's own ValueChanged
        // doesn't loop back into SetZoomImmediate.
        SkiaViewer.ZoomChanged += OnSkiaZoomChanged;
        OnSkiaZoomChanged(SkiaViewer.Zoom);

        UpdateStatus();
    }

    // ------------------------------------------------------------------
    //  Zoom sync: slider/label/buttons <-> SkiaImageViewer
    // ------------------------------------------------------------------

    private void OnSkiaZoomChanged(float zoom)
    {
        _suspendSliderUpdate = true;
        ZoomSlider.Value = Math.Round(zoom, 2);
        _suspendSliderUpdate = false;
        ZoomLabel.Text = FormatZoom(zoom);
    }

    private void ZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suspendSliderUpdate) return;
        SkiaViewer.SetZoomImmediate((float)e.NewValue);
    }

    private void FitBtn_Click(object sender, RoutedEventArgs e) => SkiaViewer.FitToScreen();
    private void ZoomInBtn_Click(object sender, RoutedEventArgs e) => SkiaViewer.ZoomIn();
    private void ZoomOutBtn_Click(object sender, RoutedEventArgs e) => SkiaViewer.ZoomOut();

    // ------------------------------------------------------------------
    //  Pen tools: pen toggle, color, size, clear, undo
    // ------------------------------------------------------------------

    private void PenToggle_Click(object sender, RoutedEventArgs e)
    {
        // PenCanvas.IsHitTestVisible is also bound in XAML, but
        // toggling here as well so the state is correct even if
        // the binding hasn't been applied yet (Loaded race).
        var isPen = PenToggle.IsChecked == true;
        PenCanvas.IsHitTestVisible = isPen;
        UpdateStatus();
    }

    private void ColorBtn_Click(object sender, RoutedEventArgs e)
    {
        // XAML uses RadioButton (which has GroupName for the
        // mutually-exclusive color group), but RadioButton is
        // a ToggleButton subclass, so the cast still works.
        if (sender is RadioButton btn && btn.Tag is string hex && !string.IsNullOrEmpty(hex))
        {
            _currentColor = SKColor.Parse(hex);
        }
    }

    private void SizeBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton btn && int.TryParse(btn.Content?.ToString(), out int size))
        {
            _currentSize = size;
        }
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        _committedStrokes.Clear();
        _strokeCount = 0;
        RenderOverlay();
        UpdateStatus();
    }

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (_committedStrokes.Count == 0) return;
        _committedStrokes.RemoveAt(_committedStrokes.Count - 1);
        _strokeCount = _committedStrokes.Count;
        RenderOverlay();
        UpdateStatus();
    }

    // ------------------------------------------------------------------
    //  PenCanvas mouse handlers (only fire when pen mode is on)
    // ------------------------------------------------------------------

    private void PenCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var pt = ToImageCoords(e.GetPosition(PenCanvas));
        if (pt == null) return;
        _isDrawing = true;
        _currentStroke = new List<SKPoint> { pt.Value };
        PenCanvas.CaptureMouse();
    }

    private void PenCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDrawing) return;
        if (e.LeftButton != MouseButtonState.Pressed) return;
        var pt = ToImageCoords(e.GetPosition(PenCanvas));
        if (pt == null) return;
        _currentStroke.Add(pt.Value);
        RenderOverlay();
    }

    private void PenCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDrawing) return;
        _isDrawing = false;
        PenCanvas.ReleaseMouseCapture();
        if (_currentStroke.Count >= 2)
        {
            _committedStrokes.Add(new StrokeData(new List<SKPoint>(_currentStroke), _currentColor, _currentSize));
            _strokeCount++;
        }
        _currentStroke = null;
        RenderOverlay();
        UpdateStatus();
    }

    private void PenCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Forward wheel events to the SkiaImageViewer with the
        // cursor position as the zoom origin. This keeps wheel
        // zoom working in pen mode (when PenCanvas is the
        // hit-testable element under the cursor).
        var pos = e.GetPosition(SkiaViewer);
        SkiaViewer.ZoomAtPoint(e.Delta, pos.X, pos.Y);
        e.Handled = true;
    }

    // ------------------------------------------------------------------
    //  Output actions: Save / Copy / OCR / Cancel / Done
    // ------------------------------------------------------------------

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        using var final = BuildFinalImage();
        CaptureEngine.SaveToTempPng(final, out var path);
        StatusText.Text = $"Saved: {path}";
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        using var final = BuildFinalImage();
        var src = CaptureEngine.BitmapToBitmapSource(final);
        src.Freeze();
        try
        {
            Clipboard.SetImage(src);
            StatusText.Text = "Copied to clipboard";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Copy failed: {ex.Message}";
        }
    }

    private async void Ocr_Click(object sender, RoutedEventArgs e)
    {
        OcrBtn.IsEnabled = false;
        StatusText.Text = "OCR running...";
        try
        {
            using var final = BuildFinalImage();
            CaptureEngine.SaveToTempPng(final, out var path);
            await OcrIntegration.RunOcrAsync(path);
            StatusText.Text = "OCR done — result in clipboard + toast";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"OCR failed: {ex.Message}";
        }
        finally
        {
            OcrBtn.IsEnabled = true;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        // Done = copy to clipboard + close. Saving to disk is
        // an explicit user action (the Save button), so we
        // don't auto-save on Done.
        Copy_Click(sender, e);
        DialogResult = true;
        Close();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { DialogResult = false; Close(); }
        else if (e.Key == Key.Enter)
        {
            // Enter only commits when not in pen mode and not
            // currently drawing, so users don't accidentally
            // close the editor mid-stroke.
            if (PenToggle.IsChecked != true && !_isDrawing)
            {
                Ok_Click(sender, new RoutedEventArgs());
            }
        }
    }

    // ------------------------------------------------------------------
    //  Internal helpers
    // ------------------------------------------------------------------

    /// <summary>
    /// Convert a point in the PenCanvas's coordinate system
    /// (screen pixels) to image coordinates (source bitmap
    /// pixels), accounting for the SkiaImageViewer's current
    /// zoom and offset. Returns null if the point lies outside
    /// the image.
    /// </summary>
    private SKPoint? ToImageCoords(System.Windows.Point pt)
    {
        var zoom = SkiaViewer.Zoom;
        if (zoom < 0.001f) return null;
        var worldX = (pt.X - SkiaViewer.OffsetX) / zoom;
        var worldY = (pt.Y - SkiaViewer.OffsetY) / zoom;
        if (worldX < 0 || worldY < 0 || worldX >= _width || worldY >= _height) return null;
        return new SKPoint((float)worldX, (float)worldY);
    }

    /// <summary>
    /// Re-render the entire overlay (clear + replay all
    /// committed strokes + the in-progress stroke if any).
    /// Triggered by any change to the stroke list or current
    /// stroke. Single SkiaSharp canvas operation, so it stays
    /// fast for typical stroke counts.
    /// </summary>
    private void RenderOverlay()
    {
        if (_overlaySkBitmap == null) return;
        using (var canvas = new SKCanvas(_overlaySkBitmap))
        {
            canvas.Clear(SKColors.Transparent);
            foreach (var stroke in _committedStrokes)
            {
                DrawStroke(canvas, stroke);
            }
            if (_isDrawing && _currentStroke.Count >= 2)
            {
                DrawStroke(canvas, new StrokeData(_currentStroke, _currentColor, _currentSize));
            }
        }
        SkiaViewer.InvalidateVisual();
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

    /// <summary>
    /// Build the final composed GDI+ Bitmap (source + overlay)
    /// for Save/Copy/OCR. The overlay is converted from SKBitmap
    /// to GDI+ via a single byte copy. The returned bitmap is
    /// owned by the caller (use with `using`).
    /// </summary>
    private Bitmap BuildFinalImage()
    {
        var result = new Bitmap(_width, _height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(result))
        {
            g.DrawImage(_originalBitmap, 0, 0);
            using var overlayGdi = SkBitmapToGdi(_overlaySkBitmap);
            g.DrawImage(overlayGdi, 0, 0);
        }
        return result;
    }

    private static unsafe SKBitmap ToSkBitmap(Bitmap bitmap)
    {
        var w = bitmap.Width;
        var h = bitmap.Height;
        var data = bitmap.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            var size = data.Height * data.Stride;
            var sk = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
            // BGRA8888 in SkiaSharp matches the GDI+ 32bppArgb
            // byte order (B, G, R, A). The pre-multiplied alpha
            // matches GDI+'s default for Format32bppArgb too, so
            // the bytes can be copied directly with no swizzle.
            Buffer.MemoryCopy(data.Scan0.ToPointer(), sk.GetPixels().ToPointer(), size, size);
            return sk;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private static unsafe Bitmap SkBitmapToGdi(SKBitmap skBitmap)
    {
        var w = skBitmap.Width;
        var h = skBitmap.Height;
        var result = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var data = result.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            var size = data.Height * data.Stride;
            Buffer.MemoryCopy(skBitmap.GetPixels().ToPointer(), data.Scan0.ToPointer(), size, size);
        }
        finally
        {
            result.UnlockBits(data);
        }
        return result;
    }

    private void UpdateStatus()
    {
        var mode = (PenToggle.IsChecked == true) ? "Pen" : "View";
        StatusText.Text = $"{mode} · {_strokeCount} stroke(s) · {_width}×{_height} · {FormatZoom(SkiaViewer.Zoom)}";
    }

    private static string FormatZoom(float zoom) => $"{Math.Round(zoom * 100)}%";
}
