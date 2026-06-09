using System;
using System.Windows;
using System.Windows.Controls;

namespace ApertureNeo.Controls;

public class AutoFitPanel : Panel
{
    public static readonly DependencyProperty MinItemSizeProperty =
        DependencyProperty.Register(nameof(MinItemSize), typeof(double), typeof(AutoFitPanel),
            new FrameworkPropertyMetadata(150.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty SpacingProperty =
        DependencyProperty.Register(nameof(Spacing), typeof(double), typeof(AutoFitPanel),
            new FrameworkPropertyMetadata(6.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public double MinItemSize
    {
        get => (double)GetValue(MinItemSizeProperty);
        set => SetValue(MinItemSizeProperty, value);
    }

    public double Spacing
    {
        get => (double)GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 1000 : availableSize.Width;
        if (width < 1) width = 1;

        int cols = Math.Max(1, (int)(width / (MinItemSize + Spacing)));
        double itemWidth = (width - (cols - 1) * Spacing) / cols;
        if (itemWidth < 1) itemWidth = 1;

        double maxHeight = 0;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(itemWidth, double.PositiveInfinity));
            if (child.DesiredSize.Height > maxHeight) maxHeight = child.DesiredSize.Height;
        }

        int rows = InternalChildren.Count == 0 ? 0 : (int)Math.Ceiling((double)InternalChildren.Count / cols);
        double totalHeight = rows * maxHeight + Math.Max(0, rows - 1) * Spacing;
        return new Size(width, totalHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var width = double.IsInfinity(finalSize.Width) ? 1000 : finalSize.Width;
        if (width < 1) width = 1;

        int cols = Math.Max(1, (int)(width / (MinItemSize + Spacing)));
        double itemWidth = (width - (cols - 1) * Spacing) / cols;
        if (itemWidth < 1) itemWidth = 1;

        for (int i = 0; i < InternalChildren.Count; i++)
        {
            int col = i % cols;
            int row = i / cols;
            double x = col * (itemWidth + Spacing);
            double y = row * (InternalChildren[i].DesiredSize.Height + Spacing);
            InternalChildren[i].Arrange(new Rect(x, y, itemWidth, InternalChildren[i].DesiredSize.Height));
        }
        return finalSize;
    }
}
