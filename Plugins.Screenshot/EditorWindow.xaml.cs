using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using SkiaSharp;

namespace ApertureNeo.Plugins.Screenshot;

public partial class EditorWindow : Window
{
    private readonly Bitmap _originalBitmap;
    private readonly Bitmap _overlayBitmap;
    private readonly int _width, _height;
    private bool _isDrawing;
    private List<SKPoint> _currentStroke = new();
    private int _strokeCount;

    public EditorWindow(Bitmap capturedBitmap)
    {
        InitializeComponent();
        _originalBitmap = capturedBitmap;
        _width = capturedBitmap.Width;
        _height = capturedBitmap.Height;

        _overlayBitmap = new Bitmap(_width, _height, PixelFormat.Format32bppArgb);
        using var g = System.Drawing.Graphics.FromImage(_overlayBitmap);
        g.Clear(System.Drawing.Color.Transparent);

        ViewportCanvas.LostMouseCapture += (_, _) => _isDrawing = false;

        Loaded += (_, _) => SafeRenderPreview();
        SizeChanged += (_, _) => SafeRenderPreview();
    }

    private void SafeRenderPreview()
    {
        try { RenderPreview(); }
        catch (Exception ex) { LogError("EditorWindow.RenderPreview", ex); }
    }

    private static void LogError(string source, Exception ex)
    {
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "ApertureNeo");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "screenshot-error.log");
            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {source}\n{ex}\n\n");
        }
        catch { }
    }

    private void RenderPreview()
    {
        if (_width < 1 || _height < 1) return;
        using var composite = new Bitmap(_width, _height, PixelFormat.Format32bppArgb);
        using (var g = System.Drawing.Graphics.FromImage(composite))
        {
            g.DrawImage(_originalBitmap, 0, 0);
            g.DrawImage(_overlayBitmap, 0, 0);
        }
        var src = CaptureEngine.BitmapToBitmapSource(composite);
        src.Freeze();
        ViewportImage.Source = src;
    }

    private Bitmap BuildFinalImage()
    {
        var result = new Bitmap(_width, _height, PixelFormat.Format32bppArgb);
        using var g = System.Drawing.Graphics.FromImage(result);
        g.DrawImage(_originalBitmap, 0, 0);
        g.DrawImage(_overlayBitmap, 0, 0);
        return result;
    }

    private SKPoint? ToImageCoords(System.Windows.Point pt)
    {
        var w = ViewportImage.ActualWidth;
        var h = ViewportImage.ActualHeight;
        if (w < 1 || h < 1) return null;

        var scale = Math.Min(_width / w, _height / h);
        var imgW = _width / scale;
        var imgH = _height / scale;
        var offsetX = (w - imgW) / 2;
        var offsetY = (h - imgH) / 2;

        var ix = (pt.X - offsetX) * scale;
        var iy = (pt.Y - offsetY) * scale;
        if (ix < 0 || iy < 0 || ix >= _width || iy >= _height) return null;

        return new SKPoint((float)ix, (float)iy);
    }

    private SKColor SelectedColor
    {
        get
        {
            if (ColorPicker.SelectedItem is System.Windows.Controls.ComboBoxItem item &&
                item.Foreground is System.Windows.Media.SolidColorBrush scb)
                return new SKColor(scb.Color.R, scb.Color.G, scb.Color.B, scb.Color.A);
            return SKColors.Red;
        }
    }

    private float PenSize => (float)SizeSlider.Value;

    private void Canvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        var pt = ToImageCoords(e.GetPosition(ViewportCanvas));
        if (pt == null) return;
        _isDrawing = true;
        _currentStroke = new List<SKPoint> { pt.Value };
        ViewportCanvas.CaptureMouse();
    }

    private void Canvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDrawing) return;
        if (e.LeftButton != MouseButtonState.Pressed) return;
        var pt = ToImageCoords(e.GetPosition(ViewportCanvas));
        if (pt == null) return;
        _currentStroke.Add(pt.Value);
        RenderStrokeToOverlay(_currentStroke);
        RenderPreview();
    }

    private void Canvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDrawing) return;
        _isDrawing = false;
        ViewportCanvas.ReleaseMouseCapture();
        _strokeCount++;
        StatusText.Content = $"{_strokeCount} stroke(s)";
        RenderPreview();
    }

    private void RenderStrokeToOverlay(List<SKPoint> points)
    {
        if (points.Count < 2 || _overlayBitmap == null) return;

        using var surface = SKSurface.Create(new SKImageInfo(_width, _height));
        var canvas = surface.Canvas;

        using (var existing = SKBitmap.FromImage(SKImage.FromEncodedData(OverlayToBytes())))
            canvas.DrawBitmap(existing, 0, 0);

        using var paint = new SKPaint
        {
            Color = SelectedColor,
            StrokeWidth = PenSize,
            Style = SKPaintStyle.Stroke,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
            IsAntialias = true,
        };

        using var path = new SKPath();
        path.MoveTo(points[0]);
        for (int i = 1; i < points.Count; i++)
            path.LineTo(points[i]);
        canvas.DrawPath(path, paint);

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var ms = new MemoryStream(data.ToArray());
        using var newOverlay = new Bitmap(ms);
        using var g = System.Drawing.Graphics.FromImage(_overlayBitmap);
        g.Clear(System.Drawing.Color.Transparent);
        g.DrawImage(newOverlay, 0, 0);
    }

    private byte[] OverlayToBytes()
    {
        using var ms = new MemoryStream();
        _overlayBitmap.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    private void PenToggle_Click(object sender, RoutedEventArgs e)
    {
        StatusText.Content = PenToggle.IsChecked == true ? "Pen mode" : "View mode";
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        using var g = System.Drawing.Graphics.FromImage(_overlayBitmap);
        g.Clear(System.Drawing.Color.Transparent);
        _strokeCount = 0;
        _currentStroke.Clear();
        StatusText.Content = "Cleared";
        RenderPreview();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        using var final = BuildFinalImage();
        CaptureEngine.SaveToTempPng(final, out var path);
        StatusText.Content = $"Saved: {path}";
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        using var final = BuildFinalImage();
        var src = CaptureEngine.BitmapToBitmapSource(final);
        src.Freeze();
        try { Clipboard.SetImage(src); StatusText.Content = "Copied to clipboard"; }
        catch (Exception ex) { StatusText.Content = $"Copy failed: {ex.Message}"; }
    }

    private async void Ocr_Click(object sender, RoutedEventArgs e)
    {
        OcrBtn.IsEnabled = false;
        StatusText.Content = "OCR running...";
        try
        {
            using var final = BuildFinalImage();
            CaptureEngine.SaveToTempPng(final, out var path);
            await OcrIntegration.RunOcrAsync(path);
            StatusText.Content = "OCR done - result in clipboard + toast";
        }
        catch (Exception ex) { StatusText.Content = $"OCR failed: {ex.Message}"; }
        finally { OcrBtn.IsEnabled = true; }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
    private void Ok_Click(object sender, RoutedEventArgs e) { Copy_Click(sender, e); DialogResult = true; Close(); }
    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { DialogResult = false; Close(); }
    }
}
