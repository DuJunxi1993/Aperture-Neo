using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ApertureNeo.Controls;

/// <summary>
/// Attaches to a Border (or any FrameworkElement) and, while IsEnabled
/// is true, translates the element up by 1px on mouse-over and back
/// on mouse-leave. Linear-style micro-lift hover affordance for
/// thumbnail cards. The transform doesn't affect layout (RenderTransform
/// is purely visual), so neighbouring thumbnails don't reflow.
///
/// Attach on the Card Border:
///   behaviors:HoverLiftBehavior.IsEnabled="True"
///   behaviors:HoverLiftBehavior.LiftAmount="-1.5"
/// </summary>
public static class HoverLiftBehavior
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled", typeof(bool), typeof(HoverLiftBehavior),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static readonly DependencyProperty LiftAmountProperty =
        DependencyProperty.RegisterAttached(
            "LiftAmount", typeof(double), typeof(HoverLiftBehavior),
            new PropertyMetadata(-1.5));

    public static void SetIsEnabled(DependencyObject d, bool v) => d.SetValue(IsEnabledProperty, v);
    public static bool GetIsEnabled(DependencyObject d) => (bool)d.GetValue(IsEnabledProperty);

    public static void SetLiftAmount(DependencyObject d, double v) => d.SetValue(LiftAmountProperty, v);
    public static double GetLiftAmount(DependencyObject d) => (double)d.GetValue(LiftAmountProperty);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement fe) return;
        if ((bool)e.NewValue)
        {
            fe.MouseEnter += OnMouseEnter;
            fe.MouseLeave += OnMouseLeave;
            // Start with the resting transform so the first enter animates
            // from a known baseline.
            fe.RenderTransform = new TranslateTransform(0, 0);
        }
        else
        {
            fe.MouseEnter -= OnMouseEnter;
            fe.MouseLeave -= OnMouseLeave;
        }
    }

    private static void OnMouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is not FrameworkElement fe) return;
        var lift = GetLiftAmount(fe);
        var anim = new DoubleAnimation(0, lift, TimeSpan.FromMilliseconds(150))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        if (fe.RenderTransform is not TranslateTransform)
            fe.RenderTransform = new TranslateTransform(0, 0);
        fe.RenderTransform.BeginAnimation(TranslateTransform.YProperty, anim);
    }

    private static void OnMouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is not FrameworkElement fe) return;
        var anim = new DoubleAnimation(fe.RenderTransform is TranslateTransform tt ? tt.Y : 0, 0, TimeSpan.FromMilliseconds(150))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        fe.RenderTransform.BeginAnimation(TranslateTransform.YProperty, anim);
    }
}
