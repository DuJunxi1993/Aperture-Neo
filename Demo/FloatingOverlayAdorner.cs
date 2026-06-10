using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using ApertureNeo.Controls;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;
using Separator = System.Windows.Controls.Separator;
using Orientation = System.Windows.Controls.Orientation;

namespace ApertureNeo.Demo;

/// <summary>
/// Base class for the three floating overlays (floating bar, info pill, status pill).
/// Inheriting from Adorner gives us automatic tracking of the adorned element
/// (here: the SkiaImageViewer) when the parent window moves or resizes —
/// the same mechanism WPF uses for selection handles, drag adorners, etc.
///
/// Why we use Adorner instead of Popup:
/// 1. The Adorner is part of the visual tree of the main window, so it follows
///    window moves automatically — no separate HWND to track.
/// 2. AdornerLayer renders ABOVE the adorned element, so the SkiaImageViewer's
///    WriteableBitmap occlusion bug does not affect us.
/// 3. No WS_POPUP / WS_EX_LAYERED — no transparency / click-through / DPI issues.
/// </summary>
public abstract class FloatingOverlayAdorner : Adorner
{
    private readonly AdornerLayer _layer;
    private readonly Grid _host;
    private double _leftOffset;
    private double _topOffset;
    private HorizontalAlignment _hAlign = HorizontalAlignment.Center;
    private VerticalAlignment _vAlign = VerticalAlignment.Bottom;

    protected FloatingOverlayAdorner(UIElement adorned)
        : base(adorned)
    {
        var content = BuildContent();
        // Wrap in a Grid that fills the Adorner bounds; the content uses
        // HorizontalAlignment/VerticalAlignment + Margin to position itself
        // within the Grid. This avoids recursive Measure/Arrange.
        _host = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
        if (content is FrameworkElement fe)
        {
            fe.HorizontalAlignment = _hAlign;
            fe.VerticalAlignment = _vAlign;
            fe.Margin = new Thickness(_leftOffset, _topOffset, Math.Abs(_leftOffset), Math.Abs(_topOffset));
        }
        _host.Children.Add(content);
        AddVisualChild(_host);
        _layer = AdornerLayer.GetAdornerLayer(adorned);
        _layer.Add(this);
    }

    public void Detach()
    {
        _layer.Remove(this);
    }

    /// <summary>Each subclass builds its UI and returns the root visual.</summary>
    protected abstract UIElement BuildContent();

    protected override int VisualChildrenCount => 1;
    protected override Visual GetVisualChild(int index) => _host;

    /// <summary>Set how the overlay is positioned relative to the adorned element.</summary>
    protected void SetAlignment(HorizontalAlignment h, VerticalAlignment v,
        double leftMargin = 0, double topMargin = 0)
    {
        _hAlign = h;
        _vAlign = v;
        _leftOffset = leftMargin;
        _topOffset = topMargin;
        InvalidateArrange();
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        // Let WPF arrange our visual children using standard layout — the
        // Grid host stretches to fill the Adorner bounds, and its child
        // positions itself via HorizontalAlignment + Margin.
        return base.ArrangeOverride(finalSize);
    }
}

/// <summary>Floating toolbar anchored at bottom-center, 18px above the viewer's bottom edge.</summary>
public sealed class FloatingBarAdorner : FloatingOverlayAdorner
{
    public Button BtnPrev { get; private set; } = null!;
    public Button BtnNext { get; private set; } = null!;
    public Button BtnFit { get; private set; } = null!;
    public Button BtnSlideshow { get; private set; } = null!;
    public Button BtnFullscreen { get; private set; } = null!;
    public SymbolIcon SlideshowIcon { get; private set; } = null!;
    public TextBlock ImageIndexInfo { get; private set; } = null!;
    public TextBlock ZoomTextBlock { get; private set; } = null!;

    public FloatingBarAdorner(SkiaImageViewer adorned) : base(adorned)
    {
        SetAlignment(HorizontalAlignment.Center, VerticalAlignment.Bottom, 0, 18);
    }

    protected override UIElement BuildContent()
    {
        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x0f, 0x10, 0x11)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(6, 4, 6, 4),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 20,
                ShadowDepth = 0,
                Opacity = 0.5,
                Color = Colors.Black
            }
        };

        BtnPrev = MakeButton("BtnPrev", SymbolRegular.ChevronLeft20, "上一张 (←)");
        BtnNext = MakeButton("BtnNext", SymbolRegular.ChevronRight20, "下一张 (→)", primary: true);

        ImageIndexInfo = new TextBlock
        {
            Text = "0/0",
            FontFamily = (FontFamily)Application.Current.Resources["FontMono"],
            FontSize = 12,
            FontWeight = FontWeights.Medium,
            Foreground = (Brush)Application.Current.Resources["TextSecondary"],
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 10, 0),
            MinWidth = 48,
            TextAlignment = TextAlignment.Center
        };

        BtnFit = MakeButton("BtnFit", SymbolRegular.PageFit20, "适应 (Ctrl+0)");

        ZoomTextBlock = new TextBlock
        {
            Text = "100%",
            FontFamily = (FontFamily)Application.Current.Resources["FontMono"],
            FontSize = 12,
            FontWeight = FontWeights.Medium,
            Foreground = (Brush)Application.Current.Resources["TextPrimary"],
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 8, 0),
            MinWidth = 44,
            TextAlignment = TextAlignment.Center,
            Cursor = Cursors.Hand
        };

        BtnSlideshow = new Button
        {
            Style = (Style)Application.Current.Resources["LinearFloatingButton"],
            ToolTip = "幻灯片 (F5)"
        };
        SlideshowIcon = new SymbolIcon { Symbol = SymbolRegular.Play20, FontSize = 12 };
        BtnSlideshow.Content = SlideshowIcon;

        BtnFullscreen = MakeButton("BtnFullscreen", SymbolRegular.FullScreenMaximize24, "全屏 (Ctrl+F)");

        var sep1 = MakeSeparator(); sep1.Margin = new Thickness(8, 6, 8, 6);
        var sep2 = MakeSeparator(); sep2.Margin = new Thickness(6, 6, 6, 6);

        var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(BtnPrev);
        panel.Children.Add(ImageIndexInfo);
        panel.Children.Add(BtnNext);
        panel.Children.Add(sep1);
        panel.Children.Add(BtnFit);
        panel.Children.Add(ZoomTextBlock);
        panel.Children.Add(sep2);
        panel.Children.Add(BtnSlideshow);
        panel.Children.Add(BtnFullscreen);

        border.Child = panel;
        return border;
    }

    private static Separator MakeSeparator() => new()
    {
        Width = 1,
        Background = new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF))
    };

    private static Button MakeButton(string name, SymbolRegular symbol, string tooltip, bool primary = false)
    {
        var btn = new Button
        {
            Name = name,
            Style = (Style)Application.Current.Resources[primary ? "LinearFloatingPrimary" : "LinearFloatingButton"],
            ToolTip = tooltip,
            Content = new SymbolIcon { Symbol = symbol, FontSize = 14 }
        };
        return btn;
    }
}

/// <summary>Info pill anchored at top-right, 14px margin. Shows image dimensions + file size.</summary>
public sealed class InfoPillAdorner : FloatingOverlayAdorner
{
    public TextBlock ImageInfo { get; private set; } = null!;

    public InfoPillAdorner(SkiaImageViewer adorned) : base(adorned)
    {
        SetAlignment(HorizontalAlignment.Right, VerticalAlignment.Top, 14, 14);
    }

    protected override UIElement BuildContent()
    {
        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x0f, 0x10, 0x11)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(999),
            Padding = new Thickness(10, 5, 10, 5)
        };
        ImageInfo = new TextBlock
        {
            Text = "",
            FontSize = 12,
            FontWeight = FontWeights.Medium,
            FontFamily = (FontFamily)Application.Current.Resources["FontMono"],
            Foreground = (Brush)Application.Current.Resources["TextSecondary"]
        };
        border.Child = ImageInfo;
        return border;
    }
}

/// <summary>Status pill anchored at top-left, 14px margin. Transient notifications.</summary>
public sealed class StatusPillAdorner : FloatingOverlayAdorner
{
    public TextBlock StatusText { get; private set; } = null!;

    public StatusPillAdorner(SkiaImageViewer adorned) : base(adorned)
    {
        SetAlignment(HorizontalAlignment.Left, VerticalAlignment.Top, 14, 14);
    }

    protected override UIElement BuildContent()
    {
        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x0f, 0x10, 0x11)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(999),
            Padding = new Thickness(10, 5, 10, 5)
        };
        StatusText = new TextBlock
        {
            FontSize = 12,
            FontWeight = FontWeights.Medium,
            Foreground = (Brush)Application.Current.Resources["TextTertiary"]
        };
        border.Child = StatusText;
        return border;
    }
}