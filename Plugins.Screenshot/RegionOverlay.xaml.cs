using System;
using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shapes;

namespace ApertureNeo.Plugins.Screenshot;

public partial class RegionOverlay : Window
{
    private System.Windows.Point _start;
    private System.Windows.Point _end;
    private bool _isDragging;

    /// <summary>Returned selection rect in virtual screen coordinates (DPI-aware).</summary>
    public System.Drawing.Rectangle? SelectedRegion { get; private set; }

    public RegionOverlay()
    {
        InitializeComponent();
        Loaded += (_, _) => UpdateDimRects();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        _start = e.GetPosition(this);
        _end = _start;
        _isDragging = true;
        SelRect.Visibility = Visibility.Visible;
        CaptureMouse();
        base.OnMouseLeftButtonDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!_isDragging) return;
        _end = e.GetPosition(this);
        UpdateSelectionRect();
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!_isDragging) return;
        _isDragging = false;
        ReleaseMouseCapture();
        _end = e.GetPosition(this);
        UpdateSelectionRect();
        CommitSelection();
        base.OnMouseLeftButtonUp(e);
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

        if (sw < 1 || sh < 1)
        {
            // No selection — dim entire screen
            Canvas.SetLeft(DimTop, 0); Canvas.SetTop(DimTop, 0); DimTop.Width = w; DimTop.Height = h;
            DimBottom.Width = 0; DimLeft.Width = 0; DimRight.Width = 0;
            return;
        }

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
        if (w < 2 || h < 2) return;

        // Convert WPF window coordinates to virtual screen coordinates
        // (the window is maximized, so it matches virtual screen)
        SelectedRegion = new System.Drawing.Rectangle(x, y, w, h);
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { DialogResult = false; Close(); }
        if (e.Key == Key.Enter && SelectedRegion.HasValue) { DialogResult = true; Close(); }
    }
}
