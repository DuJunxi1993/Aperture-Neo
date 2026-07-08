using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using ApertureNeo.Controls.Annotation;
using ApertureNeo.Plugins.Ocr.Core.Models;
using ApertureNeo.Plugins.Ocr.Core.Services;
using ApertureNeo.Plugins.Ocr.Ui;
using ApertureNeo.Services;
using SkiaSharp;

namespace ApertureNeo.Plugins.Screenshot;

/// <summary>
/// Annotation editor for a captured screenshot. Uses
/// <see cref="SkiaImageViewer"/> for image display and zoom/pan,
/// and the shared <see cref="AnnotationState"/> from
/// ApertureNeo.Controls.Annotation for stroke management. The
/// state's <c>OverlayBitmap</c> is what the viewer composites on
/// top of the source image.
///
/// The state-management code (pen canvas mouse events, color /
/// size pickers, clear/undo) was previously inlined here; it
/// has been refactored into <see cref="AnnotationState"/> so the
/// same code path runs in the editor and the main app's
/// annotation mode (P5 architectural cleanup).
/// </summary>
public partial class EditorWindow : Window
{
    private readonly Bitmap _originalBitmap;
    private readonly SKBitmap _originalSkBitmap;
    private readonly AnnotationState _annotationState;
    private readonly int _width, _height;
    private readonly ISettingsStore? _settings;

    // Local mirror of the in-progress flag. AnnotationState holds
    // its own _inProgress field but doesn't expose a public
    // IsDrawing flag, so we keep a local one for the Enter-key
    // safety check in Window_KeyDown.
    private bool _isDrawing;

    // Count of committed strokes for the status bar. The state
    // exposes CommittedStrokes.Count but a local cache avoids
    // hitting the list on every status update.
    private int _strokeCount;

    // Guard against re-entrant slider/zoom updates. When the
    // SkiaViewer's ZoomChanged handler writes ZoomSlider.Value,
    // that fires ValueChanged which would otherwise call back
    // into SetZoomImmediate. The flag breaks the cycle.
    private bool _suspendSliderUpdate;

    // Pen mode: Color (default) draws strokes into
    // AnnotationState.OverlayBitmap; Mosaic applies a
    // pixelation effect directly to the source SKBitmap (and
    // tracks the original pixel data per stroke for undo).
    private enum PenMode { Color, Mosaic }
    private PenMode _penMode = PenMode.Color;

    // Mosaic-mode state: just two points (start + current drag
    // tip). The dragged rectangle itself is the mosaic target —
    // much simpler than collecting all stroke points and using
    // the bounding box. The per-stroke saved data is for undo.
    private SKPoint _mosaicStart;
    private SKPoint _mosaicCurrent;
    private bool _isDraggingMosaic;
    private readonly List<(SKRectI Rect, byte[] OriginalData)> _mosaicHistory = new();

    public EditorWindow(Bitmap capturedBitmap, ISettingsStore? settings = null)
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

        _settings = settings;
        _originalBitmap = capturedBitmap;
        _width = capturedBitmap.Width;
        _height = capturedBitmap.Height;

        // Convert the GDI+ bitmap once into an SKBitmap that
        // the SkiaImageViewer can render directly. The GDI+
        // bitmap stays alive for BuildFinalImage (Save/Copy/OCR
        // all need a GDI+ Bitmap for the existing helpers).
        _originalSkBitmap = ToSkBitmap(capturedBitmap);

        // Shared annotation state. Owns the overlay SKBitmap and
        // the stroke list. The viewer reads the OverlayBitmap
        // property; mouse events on PenCanvas route through
        // BeginStroke/ExtendStroke/CommitStroke below.
        _annotationState = new AnnotationState(_width, _height);

        // State redraws its overlay internally; we just need to
        // tell the viewer to re-upload the bitmap. Subscribe once.
        _annotationState.RedrawRequested += (_, _) => SkiaViewer.InvalidateOverlay();

        // Defensive: if capture is lost (Alt+Tab, etc.) the
        // drawing state machine must reset. Without this, a
        // half-drawn stroke could leave _isDrawing stuck on.
        PenCanvas.LostMouseCapture += (_, _) => { _isDrawing = false; };
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // The annotation toolbar is now inlined in the XAML top
        // bar; it reads/writes _annotationState directly. No
        // separate AnnotationBar hookup needed.

        // Hand the bitmaps to the SkiaImageViewer. The viewer
        // calls FitToScreen inside LoadBitmap (no animation), so
        // the editor opens already framed correctly.
        SkiaViewer.LoadBitmap(_originalSkBitmap);
        SkiaViewer.OverlayBitmap = _annotationState.OverlayBitmap;

        // Sync UI to the viewer's state. ZoomChanged fires on
        // any zoom change; we route it back to the slider + label
        // with a re-entrancy guard so the slider's own ValueChanged
        // doesn't loop back into SetZoomImmediate.
        SkiaViewer.ZoomChanged += OnSkiaZoomChanged;
        OnSkiaZoomChanged(SkiaViewer.Zoom);

        // Initial pen mode is Color (matches the RadioButton's
        // IsChecked="True" in the segmented control). Make sure
        // the color picker is enabled accordingly.
        SetColorPickerEnabled(true);

        UpdateStatus();
    }

    /// <summary>
    /// Self-managed window drag (matches the main app's title bar
    /// drag-move). The handler is on the TOP BAR Border (not the
    /// whole window) so viewer clicks don't trigger drag-move at
    /// all. If the click originated on a button (or button's
    /// content — the visual tree walk doesn't reach the button
    /// for templated controls, so also check TemplatedParent),
    /// let the button handle the click instead of starting a drag.
    /// Double-click toggles maximize.
    /// </summary>
    private void TopBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // Skip drag if the click originated on a button (directly
        // or via its template's content). OriginalSource can be the
        // Button itself, or a child of the Button's template
        // (ContentPresenter / TextBlock for text buttons, or
        // Ellipse / Grid for the color circle style). The visual
        // tree walk finds the Button for direct hits; the
        // TemplatedParent check catches the rest.
        if (e.OriginalSource is ButtonBase) return;
        if (e.OriginalSource is FrameworkElement fe && fe.TemplatedParent is ButtonBase) return;

        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }

        try { DragMove(); } catch { /* released outside the window — safe to ignore */ }
    }

    /// <summary>Toggle PenType between Color and Mosaic. In
    /// Mosaic mode the color picker is grayed out (IsEnabled=false
    /// — the default RadioButton disabled visual applies opacity);
    /// the size selector stays enabled because its value means
    /// "mosaic block size" in mosaic mode.</summary>
    private void PenModeColor_Checked(object sender, RoutedEventArgs e)
    {
        if (_penMode == PenMode.Color) return;
        _penMode = PenMode.Color;
        SetColorPickerEnabled(true);
        UpdateStatus();
    }

    private void PenModeMosaic_Checked(object sender, RoutedEventArgs e)
    {
        if (_penMode == PenMode.Mosaic) return;
        _penMode = PenMode.Mosaic;
        SetColorPickerEnabled(false);
        UpdateStatus();
    }

    private void SetColorPickerEnabled(bool enabled)
    {
        ColorRedBtn.IsEnabled = enabled;
        ColorOrangeBtn.IsEnabled = enabled;
        ColorYellowBtn.IsEnabled = enabled;
        ColorGreenBtn.IsEnabled = enabled;
        ColorBlueBtn.IsEnabled = enabled;
        ColorBlackBtn.IsEnabled = enabled;
        ColorWhiteBtn.IsEnabled = enabled;
    }

    private void ToggleMaximize()
    {
        if (WindowState == WindowState.Maximized)
        {
            WindowState = WindowState.Normal;
            if (MaximizeIcon != null) MaximizeIcon.Text = "□";
        }
        else
        {
            WindowState = WindowState.Maximized;
            if (MaximizeIcon != null) MaximizeIcon.Text = "▢";
        }
    }

    // ------------------------------------------------------------------
    //  Title bar drag-move (Phase 4: replaces the old TopBar handler
    //  because the new layout has a separate 44px title bar that
    //  hosts the file-name label and the min/max/close buttons).
    //  The toolbar's MouseLeftButtonDown also points at this
    //  handler so the user can drag from the toolbar row too.
    //  ButtonBase / TemplatedParent-is-ButtonBase guard ensures
    //  button clicks still work (otherwise MouseLeftButtonDown would
    //  drag-move the window on every toolbar click).
    // ------------------------------------------------------------------
    private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.OriginalSource is ButtonBase) return;
        if (e.OriginalSource is FrameworkElement fe && fe.TemplatedParent is ButtonBase) return;

        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }

        try { DragMove(); } catch { /* released outside the window — safe to ignore */ }
    }

    private void MinimizeBtn_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleMaximize();
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
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
        // XAML parser can fire ValueChanged during InitializeComponent
        // when attributes are applied in order (Minimum before Value
        // coerces the default Value 0 up to the new minimum). At that
        // point the SkiaViewer x:Name field hasn't been assigned yet,
        // so any access would NRE. Skip parse-time events; the user's
        // real slider interaction only happens after the window is
        // fully loaded.
        if (SkiaViewer == null) return;
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
        // In mosaic mode the color picker is disabled (ColorBtn
        // is grayed out, so the user can't even reach this
        // handler), but we still gate here for safety — e.g. if
        // the user toggles Color → Mosaic mid-stroke.
        if (_penMode == PenMode.Mosaic) return;

        // XAML uses RadioButton (which has GroupName for the
        // mutually-exclusive color group), but RadioButton is
        // a ToggleButton subclass, so the cast still works.
        if (sender is RadioButton btn && btn.Tag is string hex && !string.IsNullOrEmpty(hex))
        {
            _annotationState.CurrentColor = SKColor.Parse(hex);
        }
    }

    private void SizeBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton btn && int.TryParse(btn.Content?.ToString(), out int size))
        {
            _annotationState.CurrentSize = size;
        }
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        // Always clear BOTH color strokes and mosaic edits, regardless
        // of the current pen mode. Earlier the handler only cleared
        // one side (whichever matched the active mode), which left
        // mosaic edits invisible to the user if they happened to be
        // in Color mode — confusing. Now: color strokes are cleared
        // first, then the underlying SKBitmap is restored to its
        // pre-mosaic state by replaying all saved rectangles in
        // reverse order (so later strokes don't overwrite earlier
        // ones' restored data).
        _annotationState.Clear();
        _strokeCount = 0;
        for (int i = _mosaicHistory.Count - 1; i >= 0; i--)
        {
            RestoreMosaicData(_mosaicHistory[i].Rect, _mosaicHistory[i].OriginalData);
        }
        _mosaicHistory.Clear();
        SkiaViewer.NotifyContentChanged();
        UpdateStatus();
    }

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        // Undo the most recent action regardless of pen mode. We pick
        // the source with the most recent entry — that gives the
        // right answer as long as the user has been alternating (or
        // even just sticking to one mode). Color and mosaic share a
        // single timeline from the user's perspective, so we just
        // pop whichever side has the bigger count.
        int colorCount = _annotationState.CommittedStrokes.Count;
        int mosaicCount = _mosaicHistory.Count;
        if (colorCount == 0 && mosaicCount == 0) return;

        if (colorCount > mosaicCount)
        {
            _annotationState.Undo();
            _strokeCount = _annotationState.CommittedStrokes.Count;
        }
        else
        {
            var (rect, data) = _mosaicHistory[^1];
            _mosaicHistory.RemoveAt(_mosaicHistory.Count - 1);
            RestoreMosaicData(rect, data);
            SkiaViewer.NotifyContentChanged();
        }
        UpdateStatus();
    }

    // ------------------------------------------------------------------
    //  PenCanvas mouse handlers (only fire when pen mode is on)
    // ------------------------------------------------------------------

    private void PenCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var pt = ToImageCoords(e.GetPosition(PenCanvas));
        if (pt == null) return;

        if (_penMode == PenMode.Mosaic)
        {
            // Start a drag-rectangle. MouseMove updates the second corner; MouseUp applies the mosaic to the rectangle. Show the live preview rectangle immediately (at zero size) so the user gets instant feedback before any drag movement.
            _mosaicStart = pt.Value;
            _mosaicCurrent = pt.Value;
            _isDraggingMosaic = true;
            _isDrawing = true;
            MosaicPreviewRect.Visibility = Visibility.Visible;
            UpdateMosaicPreview();
        }
        else
        {
            _isDrawing = true;
            _annotationState.BeginStroke();
            _annotationState.ExtendStroke(pt.Value);
        }
        PenCanvas.CaptureMouse();
    }

    private void PenCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDrawing) return;
        if (e.LeftButton != MouseButtonState.Pressed) return;
        var pt = ToImageCoords(e.GetPosition(PenCanvas));
        if (pt == null) return;

        if (_penMode == PenMode.Mosaic)
        {
            _mosaicCurrent = pt.Value;
            UpdateMosaicPreview();
        }
        else
        {
            _annotationState.ExtendStroke(pt.Value);
        }
    }

    private void PenCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDrawing) return;
        _isDrawing = false;
        PenCanvas.ReleaseMouseCapture();

        if (_penMode == PenMode.Mosaic)
        {
            if (_isDraggingMosaic) { _isDraggingMosaic = false; ApplyMosaicRect(_mosaicStart, _mosaicCurrent); MosaicPreviewRect.Visibility = Visibility.Collapsed; }
        }
        else
        {
            _annotationState.CommitStroke();
            _strokeCount = _annotationState.CommittedStrokes.Count;
        }
        UpdateStatus();
    }

    /// <summary>
    /// Apply a mosaic to the rectangle the user just dragged out in
    /// mosaic mode. Takes the two image-space corner points
    /// (start + current drag tip), expands the rect to the block
    /// grid so consecutive strokes are aligned, captures the rect's
    /// pre-mosaic pixels (BGRA8888) for Undo, applies per-block
    /// averaging to <c>_originalSkBitmap</c>, and notifies the
    /// viewer to re-render. Block size = the current size value
    /// (same selector that drives pen stroke width in color mode).
    /// </summary>
    private void ApplyMosaicRect(SKPoint start, SKPoint end)
    {
        // Compute the user-defined rect (in image coordinates).
        int minX = (int)Math.Round(Math.Min(start.X, end.X));
        int minY = (int)Math.Round(Math.Min(start.Y, end.Y));
        int maxX = (int)Math.Round(Math.Max(start.X, end.X));
        int maxY = (int)Math.Round(Math.Max(start.Y, end.Y));
        if (maxX <= minX || maxY <= minY) return;

        int blockSize = Math.Max(2, (int)Math.Round(_annotationState.CurrentSize));

        // Block-align the rect so the mosaic grid is consistent
        // across overlapping strokes.
        int x0 = (minX / blockSize) * blockSize;
        int y0 = (minY / blockSize) * blockSize;
        int x1 = ((maxX + blockSize) / blockSize) * blockSize;
        int y1 = ((maxY + blockSize) / blockSize) * blockSize;
        x0 = Math.Max(0, x0);
        y0 = Math.Max(0, y0);
        x1 = Math.Min(_width, x1);
        y1 = Math.Min(_height, y1);
        if (x1 <= x0 || y1 <= y0) return;

        var rect = new SKRectI(x0, y0, x1, y1);
        int w = rect.Width;
        int h = rect.Height;
        int stride = w * 4;
        var original = new byte[stride * h];

        // Capture the rect's pre-mosaic pixel data (BGRA8888).
        // _originalSkBitmap.GetPixel reads each pixel — slower
        // than pointer access, but fine for typical screenshot
        // rect sizes.
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                var c = _originalSkBitmap.GetPixel(rect.Left + x, rect.Top + y);
                int idx = y * stride + x * 4;
                original[idx + 0] = c.Blue;
                original[idx + 1] = c.Green;
                original[idx + 2] = c.Red;
                original[idx + 3] = c.Alpha;
            }
        }

        // Apply the mosaic: for each block, average the pixels
        // (as captured — i.e. the current state, not the
        // original), then write that average to every pixel in
        // the block. The result is a pixelated region that
        // overlays the current image content.
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
                        int imgX = rect.Left + bx + px;
                        int imgY = rect.Top + by + py;
                        _originalSkBitmap.SetPixel(imgX, imgY, avg);
                    }
                }
            }
        }

        _mosaicHistory.Add((rect, original));
        SkiaViewer.NotifyContentChanged();
    }

    /// <summary>
    /// Update the live-preview Rectangle to reflect the current
    /// drag rectangle. Converts image-space coordinates to
    /// PenCanvas coordinates using the viewer's current zoom
    /// and offset (so the preview stays aligned with the
    /// actual mosaic target the user is drawing). IsHitTestVisible
    /// on the Rectangle is False so this redraw doesn't steal
    /// mouse events from the PenCanvas. Safe to call before
    /// Loaded (the XAML element lookup is null-safe) — the
    /// preview just stays Collapsed until MouseDown shows it.
    /// </summary>
    private void UpdateMosaicPreview()
    {
        if (MosaicPreviewRect == null || SkiaViewer == null) return;
        var zoom = SkiaViewer.Zoom;
        if (zoom < 0.001f) return;
        var offX = SkiaViewer.OffsetX;
        var offY = SkiaViewer.OffsetY;

        double x1 = _mosaicStart.X * zoom + offX;
        double y1 = _mosaicStart.Y * zoom + offY;
        double x2 = _mosaicCurrent.X * zoom + offX;
        double y2 = _mosaicCurrent.Y * zoom + offY;

        var left = Math.Min(x1, x2);
        var top = Math.Min(y1, y2);
        var width = Math.Abs(x2 - x1);
        var height = Math.Abs(y2 - y1);

        Canvas.SetLeft(MosaicPreviewRect, left);
        Canvas.SetTop(MosaicPreviewRect, top);
        MosaicPreviewRect.Width = width;
        MosaicPreviewRect.Height = height;
    }

    private void RestoreMosaicData(SKRectI rect, byte[] data)
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
                    (byte)data[idx + 2], (byte)data[idx + 1],
                    (byte)data[idx + 0], (byte)data[idx + 3]);
                _originalSkBitmap.SetPixel(rect.Left + x, rect.Top + y, c);
            }
        }
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

    /// <summary>
    /// P5: Save clicked in the shared AnnotationOverlay. Routes
    /// to the same save service as the editor's own Save button
    /// (the editor's standalone Save button was removed from the
    /// XAML when the drawing toolbar migrated to the shared
    /// overlay; the overlay's Save button is now the primary).
    /// </summary>
    private void AnnotationBar_SaveRequested(object? sender, EventArgs e) => DoSave();

    private void Save_Click(object sender, RoutedEventArgs e) => DoSave();

    private void DoSave()
    {
        // P5: SaveService composes original + overlay state and
        // shows the shared SaveDialog. The editor has no
        // currentFilePath (its output is always a new file, never
        // an overwrite), so the dialog's "覆盖原图" button is
        // disabled.
        var saveService = new AnnotationSaveService(_settings);
        var outcome = saveService.Save(_originalBitmap, _annotationState, this, currentFilePath: null);
        switch (outcome)
        {
            case AnnotationSaveService.SaveOutcome.Saved:
                StatusText.Text = $"Saved: {saveService.LastSavedPath}";
                _annotationState.HasUnsavedChanges = false;
                break;
            case AnnotationSaveService.SaveOutcome.Cancelled:
                // user pressed cancel; leave status as-is
                break;
            case AnnotationSaveService.SaveOutcome.Failed:
                StatusText.Text = "Save failed";
                break;
        }
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
        var tempPath = Path.Combine(
            Path.GetTempPath(), "ApertureNeo", "screenshots",
            $"screenshot-{Guid.NewGuid():N}.png");
        try
        {
            using var final = BuildFinalImage();
            Directory.CreateDirectory(Path.GetDirectoryName(tempPath)!);
            final.Save(tempPath, ImageFormat.Png);

            // In-process OCR: no ApertureNeo.exe subprocess spawn
            // (was ~500ms), no PowerShell toast (was another spawn).
            var ocr = new OcrService();
            OcrResult result = await ocr.ExtractAsync(tempPath);

            if (!result.IsSuccess)
            {
                StatusText.Text = $"OCR failed: {result.ErrorMessage}";
                return;
            }

            // Always: copy to clipboard.
            try { Clipboard.SetText(result.FullText); } catch { }
            StatusText.Text = $"OCR done — {result.Lines.Count} lines copied to clipboard";

            // P5: open OcrResultWindow only when the user opted
            // in via the 插件 submenu's "OCR 显示结果窗口"
            // CheckBox. The default is false (clipboard only,
            // matches the "fast path" expectation).
            if (_settings?.EditorOcrShowWindow == true)
            {
                var win = new OcrResultWindow();
                win.SetResult(result, Path.GetFileName(tempPath));
                win.Owner = this;
                win.ShowDialog();
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"OCR failed: {ex.Message}";
        }
        finally
        {
            OcrBtn.IsEnabled = true;
            try { File.Delete(tempPath); } catch { }
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    /// <summary>Close (X) button. Same effect as Cancel — discards
    /// the current edit without saving. Kept as a separate handler
    /// for visual-semantic clarity (X = close-window affordance,
    /// Cancel = discard action); both end up closing the dialog
    /// with DialogResult=false so the host knows nothing was
    /// committed.</summary>
    private void Close_Click(object sender, RoutedEventArgs e)
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
    /// Build the final composed GDI+ Bitmap (source + overlay)
    /// for Copy/OCR. Save goes through
    /// <see cref="AnnotationSaveService"/> which does the same
    /// composition + dialog flow. The returned bitmap is owned
    /// by the caller (use with `using`).
    /// </summary>
    private Bitmap BuildFinalImage()
    {
        var result = new Bitmap(_width, _height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(result))
        {
            g.DrawImage(_originalBitmap, 0, 0);
            using var overlayGdi = SkBitmapToGdi(_annotationState.OverlayBitmap);
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
        var type = _penMode == PenMode.Mosaic ? "Mosaic" : "Color";
        var strokeInfo = _penMode == PenMode.Mosaic
            ? $"{_mosaicHistory.Count} mosaic"
            : $"{_strokeCount} stroke(s)";
        StatusText.Text = $"{mode} · {type} · {strokeInfo} · {_width}×{_height} · {FormatZoom(SkiaViewer.Zoom)}";
    }

    private static string FormatZoom(float zoom) => $"{Math.Round(zoom * 100)}%";
}
