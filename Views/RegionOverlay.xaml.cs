using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shapes;

namespace ApertureNeo.Views;

public partial class RegionOverlay : Window
{
    private System.Windows.Point _start;
    private System.Windows.Point _end;
    private bool _isDragging;

    public System.Drawing.Rectangle? SelectedRegion { get; private set; }
    public bool IsFullscreen { get; private set; }

    public bool OcrRequested { get; private set; }

    public bool Confirmed { get; private set; }

    public RegionOverlay()
    {
        InitializeComponent();
        System.Windows.Input.InputMethod.SetIsInputMethodEnabled(this, false);
        ConfirmBtn.IsEnabled = false;
        ActionBarText.Text = "Drag to select an area";
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateDimRects();
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
            FloatBar.Visibility = Visibility.Collapsed;
            ActionBarText.Text = "Drag to select area (too small)";
            return;
        }
        SelectedRegion = new System.Drawing.Rectangle(x, y, w, h);
        ConfirmBtn.IsEnabled = true;
        ActionBarText.Text = $"Selected: {w} x {h} px";
        FloatBar.Visibility = Visibility.Visible;
        PositionFloatBar(x, y, w, h);
    }

    private void PositionFloatBar(double selX, double selY, double selW, double selH)
    {
        var sz = new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity);
        FloatBar.Measure(sz);
        FloatBar.Arrange(new Rect(FloatBar.DesiredSize));
        var barW = FloatBar.DesiredSize.Width;
        var barH = FloatBar.DesiredSize.Height;
        const double gap = 8;
        var screenW = ActualWidth;
        var screenH = ActualHeight;

        double barX, barY;
        if (selW > barW && selH > barH)
        {
            barX = selX + selW - barW;
            barY = selY + selH - barH;
        }
        else
        {
            barX = selX + selW + gap;
            barY = selY + selH + gap;
        }
        barX = Math.Max(gap, Math.Min(barX, screenW - barW - gap));
        barY = Math.Max(gap, Math.Min(barY, screenH - barH - gap));
        Canvas.SetLeft(FloatBar, barX);
        Canvas.SetTop(FloatBar, barY);
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

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F) { FullscreenBtn_Click(this, new RoutedEventArgs()); e.Handled = true; }
        else if (e.Key == Key.O) { OcrBtn_Click(this, new RoutedEventArgs()); e.Handled = true; }
        else if (e.Key == Key.Escape) { Confirmed = false; Close(); e.Handled = true; }
        else if ((e.Key == Key.Enter || e.Key == Key.Space) && SelectedRegion.HasValue)
        { ConfirmBtn_Click(this, new RoutedEventArgs()); e.Handled = true; }
    }
}