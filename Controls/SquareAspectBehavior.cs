using System.Windows;
using System.Windows.Controls;

namespace ApertureNeo.Controls;

public static class SquareAspectBehavior
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled", typeof(bool), typeof(SquareAspectBehavior),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static void SetIsEnabled(DependencyObject obj, bool value)
        => obj.SetValue(IsEnabledProperty, value);

    public static bool GetIsEnabled(DependencyObject obj)
        => (bool)obj.GetValue(IsEnabledProperty);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Border b)
        {
            if ((bool)e.NewValue) b.SizeChanged += OnSizeChanged;
            else b.SizeChanged -= OnSizeChanged;
        }
    }

    private static void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is Border b) b.Height = b.ActualWidth;
    }
}
