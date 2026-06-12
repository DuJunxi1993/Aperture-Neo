using System;
using System.Windows;
using System.Windows.Controls;

namespace ApertureNeo.Controls;

/// <summary>
/// Attached property that keeps a <see cref="Border"/>'s height in
/// sync with its width multiplied by a binding to a numeric aspect
/// ratio (width/height). Used by the thumbnail card so the card
/// silhouette follows the image's true aspect ratio instead of
/// forcing a square.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a behavior, not a binding on Height?</b> WPF does not
/// support bindings on the <c>Height</c> *and* <c>ActualWidth</c>
/// of the same element without code-behind — the binding system
/// cannot express "my height = my width × source ratio" purely
/// declaratively because the target depends on the live
/// <c>ActualWidth</c> (a read-only property that is itself driven
/// by the layout pass). The behavior hooks <c>SizeChanged</c>
/// and re-evaluates the ratio on every layout pass.
/// </para>
/// <para>
/// <b>Width binding.</b> The ratio is supplied as a <c>BindingBase</c>
/// so the parent can pull it from a property on the data item
/// (typically <c>ImageItem.AspectRatio</c>). When the data item
/// changes its dimensions after async probing, the new ratio
/// flows in and the next <c>SizeChanged</c> resizes the card.
/// </para>
/// <para>
/// <b>Clamping.</b> If the resulting height is 0 or negative (e.g.
/// zero width during a layout edge case) we leave the existing
/// height alone rather than collapsing to invisible.
/// </para>
/// </remarks>
public static class AspectRatioBehavior
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled", typeof(bool), typeof(AspectRatioBehavior),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static void SetIsEnabled(DependencyObject obj, bool value)
        => obj.SetValue(IsEnabledProperty, value);

    public static bool GetIsEnabled(DependencyObject obj)
        => (bool)obj.GetValue(IsEnabledProperty);

    /// <summary>
    /// Binding to a numeric (double) source whose value is the
    /// width/height ratio of the displayed image. Required when
    /// <see cref="IsEnabled"/> is true.
    /// </summary>
    public static readonly DependencyProperty RatioProperty =
        DependencyProperty.RegisterAttached(
            "Ratio", typeof(double), typeof(AspectRatioBehavior),
            new PropertyMetadata(1.0, OnRatioChanged));

    public static void SetRatio(DependencyObject obj, double value)
        => obj.SetValue(RatioProperty, value);

    public static double GetRatio(DependencyObject obj)
        => (double)obj.GetValue(RatioProperty);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Border b)
        {
            if ((bool)e.NewValue) b.SizeChanged += OnSizeChanged;
            else b.SizeChanged -= OnSizeChanged;
        }
    }

    private static void OnRatioChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Border b) ApplyRatio(b, GetRatio(b), b.ActualWidth);
    }

    private static void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is Border b) ApplyRatio(b, GetRatio(b), b.ActualWidth);
    }

    private static void ApplyRatio(Border b, double ratio, double width)
    {
        if (width <= 0) return;
        var newHeight = width * ratio;
        if (newHeight <= 0) return;
        if (Math.Abs(b.Height - newHeight) > 0.5) b.Height = newHeight;
    }
}
