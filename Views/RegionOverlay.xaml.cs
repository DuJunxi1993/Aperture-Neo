using System;
using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shapes;

namespace ApertureNeo.Views;

/// <summary>
/// Fullscreen region-selection overlay. Same role as the
/// original <c>ApertureNeo.Plugins.Screenshot.RegionOverlay</c>;
/// moved to the main app so <see cref="Services.CaptureService"/>
/// can construct it directly (the screenshot plugin's shortcut
/// handler invokes the service, which shows the overlay
/// modally). Standalone callers still see this class via the
/// Plugins.Screenshot assembly's namespace forward via
/// <c>InternalsVisibleTo</c> is not used — the standalone EXE
/// keeps its own copy under the original namespace for now.
/// </summary>
public partial class RegionOverlay : Window
{
    private System.Windows.Point _start;
    private System.Windows.Point _end;
    private bool _isDragging;

    public System.Drawing.Rectangle? SelectedRegion { get; private set; }
    public bool IsFullscreen { get; private set; }

    /// <summary>Set true when the user picked the OCR button
    /// (or pressed O). <see cref="Services.CaptureService"/>
    /// reads this after ShowDialog returns to dispatch into
    /// the OCR-aware editor flow instead of the plain editor.</summary>
    public bool OcrRequested { get; private set; }

    /// <summary>True iff the user picked Confirm / Fullscreen /
    /// OCR (i.e. any "productive" choice, not Cancel / Esc).
    /// Used by callers who show this window via <see cref="Window.Show"/>
    /// (non-modal), where <see cref="Window.DialogResult"/> cannot
    /// be set without throwing <see cref="InvalidOperationException"/>.
    /// Also set for the <see cref="Window.ShowDialog"/> path so the
    /// standalone ScreenshotTool can read a single consistent flag
    /// instead of branching on the DialogResult return value.</summary>
    public bool Confirmed { get; private set; }

    public RegionOverlay()
    {
        InitializeComponent();
        ConfirmBtn.IsEnabled = false;
        ActionBarText.Text = "Drag to select an area (F = fullscreen, O = OCR, Esc = cancel)";
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateDimRects();
        // Force-activate the dialog. When Owner=null, WPF still
        // creates the HWND but Windows may not foreground it
        // (no parent HWND to chain through). Calling Activate()
        // in Loaded (after the HWND exists) explicitly brings the
        // dialog to the foreground so mouse clicks route to the
        // action bar buttons. Topmost=true keeps it on top
        // while the user picks a region.
        Activate();
    }

    private void OverlayCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _start = e.GetPosition(this);
        _end = _start;
        _isDragging = true;
        SelRect.Visibility = Visibility.Visible;
        OverlayCanvas.CaptureMouse();
        UpdateSelectionRect();
    }

    private void OverlayCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging) return;
        _end = e.GetPosition(this);
        UpdateSelectionRect();
    }

    private void OverlayCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging) return;
        var savedStart = _start;
        _isDragging = false;
        OverlayCanvas.ReleaseMouseCapture();
        _end = e.GetPosition(this);
        if (Math.Abs(_end.X - savedStart.X) < 2 && Math.Abs(_end.Y - savedStart.Y) < 2) return;
        UpdateSelectionRect();
        CommitSelection();
    }

    private void UpdateSelectionRect()
    {
        var x = Math.Min(_start.X, _end.X);
        var y = Math.Min(_start.Y, _end.Y);
        var w = Math.Abs(_end.X - _start.X);
        var h = Math.Abs(_end.Y - _start.Y);

        Canvas.SetLeft(SelRect, x);
        Canvas.SetTop(SelRect, y);
        SelRect.Width = w;
        SelRect.Height = h;
        UpdateDimRects(x, y, w, h);
    }

    private void UpdateDimRects(double sx = 0, double sy = 0, double sw = 0, double sh = 0)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w < 1 || h < 1) return;

        if (sw < 1 || sh < 1)
        {
            Canvas.SetLeft(DimTop, 0); Canvas.SetTop(DimTop, 0); DimTop.Width = w; DimTop.Height = h;
            DimBottom.Width = 0; DimLeft.Width = 0; DimRight.Width = 0;
            DimTop.Visibility = Visibility.Visible;
            DimBottom.Visibility = Visibility.Collapsed;
            DimLeft.Visibility = Visibility.Collapsed;
            DimRight.Visibility = Visibility.Collapsed;
            return;
        }

        DimTop.Visibility = Visibility.Visible;
        DimBottom.Visibility = Visibility.Visible;
        DimLeft.Visibility = Visibility.Visible;
        DimRight.Visibility = Visibility.Visible;

        DimTop.Width = w; DimTop.Height = sy;
        Canvas.SetLeft(DimTop, 0); Canvas.SetTop(DimTop, 0);

        DimBottom.Width = w; DimBottom.Height = h - sy - sh;
        Canvas.SetLeft(DimBottom, 0); Canvas.SetTop(DimBottom, sy + sh);

        DimLeft.Width = sx; DimLeft.Height = sh;
        Canvas.SetLeft(DimLeft, 0); Canvas.SetTop(DimLeft, sy);

        DimRight.Width = w - sx - sw; DimRight.Height = sh;
        Canvas.SetLeft(DimRight, sx + sw); Canvas.SetTop(DimRight, sy);
    }

    private void CommitSelection()
    {
        var x = (int)Math.Min(_start.X, _end.X);
        var y = (int)Math.Min(_start.Y, _end.Y);
        var w = (int)Math.Abs(_end.X - _start.X);
        var h = (int)Math.Abs(_end.Y - _start.Y);
        if (w < 2 || h < 2)
        {
            SelectedRegion = null;
            ConfirmBtn.IsEnabled = false;
            ActionBarText.Text = "Drag to select area (too small)";
            return;
        }
        SelectedRegion = new System.Drawing.Rectangle(x, y, w, h);
        ConfirmBtn.IsEnabled = true;
        ActionBarText.Text = $"Selected: {w} x {h} px — Confirm / F (fullscreen) / O (OCR)";
    }

    private void ConfirmBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!SelectedRegion.HasValue) return;
        IsFullscreen = false;
        Confirmed = true;
        Close();
    }

    private void FullscreenBtn_Click(object sender, RoutedEventArgs e)
    {
        IsFullscreen = true;
        Confirmed = true;
        Close();
    }

    private void OcrBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedRegion.HasValue) IsFullscreen = false;
        else IsFullscreen = true;
        OcrRequested = true;
        Confirmed = true;
        Close();
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e)
    {
        Confirmed = false;
        Close();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Confirmed = false; Close(); return; }
        if (e.Key == Key.Enter && SelectedRegion.HasValue) { ConfirmBtn_Click(this, new RoutedEventArgs()); return; }
        if (e.Key == Key.F) { FullscreenBtn_Click(this, new RoutedEventArgs()); return; }
        if (e.Key == Key.O) { OcrBtn_Click(this, new RoutedEventArgs()); return; }
    }
}