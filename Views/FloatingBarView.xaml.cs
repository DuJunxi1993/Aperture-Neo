using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using ApertureNeo.Controls;
using ApertureNeo.Models;
using ApertureNeo.ViewModels;

namespace ApertureNeo.Views;

public partial class FloatingBarView : UserControl
{
    private bool _suspendSliderUpdate;
    private SkiaImageViewer? _viewer;
    private Window? _hookedWindow;

    // ---- Proximity expand / collapse ----
    //
    // The control bar collapses into a small iOS-style handle at
    // the bottom center. Hovering the handle (or the invisible hot
    // zone strip around it) expands the bar: it scales up out of
    // the handle anchor (RenderTransformOrigin 0.5,1.1). Leaving
    // the bar collapses it back down onto the handle after a short
    // grace delay. A 30s idle fallback collapses the bar even if
    // the pointer parks over it (no MouseLeave); the zoom slider
    // popup keeps the bar expanded while it is open.
    private static readonly TimeSpan CollapseGrace = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan IdleFallbackDelay = TimeSpan.FromSeconds(30);
    private const double MoveResetThreshold = 5.0;

    private DispatcherTimer? _collapseGraceTimer;
    private DispatcherTimer? _idleFallbackTimer;
    private bool _expanded;
    private Point _lastMousePosition;

    // ---- Handle color adaptation ----
    //
    // The handle pill (dark translucent #33000000) disappears on dark
    // images. We sample the image luminance under the pill once the
    // view is stable and switch the pill to a light translucent color
    // (#33FFFFFF) on dark content. Sampling is cheap (a 5x5 GetPixel
    // grid via the viewer's inverse transform), so this only runs on
    // image load and after zoom/pan/rotate settles (200ms debounce).
    private static readonly Brush DarkHandleBrush =
        new SolidColorBrush(Color.FromArgb(0x33, 0x00, 0x00, 0x00));
    private static readonly Brush LightHandleBrush =
        new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
    private const double HandleLuminanceThreshold = 0.5;
    private static readonly TimeSpan HandleColorDebounce = TimeSpan.FromMilliseconds(200);
    private DispatcherTimer? _handleColorTimer;
    private SkiaImageViewer? _colorSubscribedViewer;

    public FloatingBarView()
    {
        InitializeComponent();
        DataContext = AppHost.Services?.GetService<FloatingBarViewModel>();

        // Hook Window-level clicks once Loaded so clicks outside the
        // zoom slider popup dismiss it (the Popup lives in its own
        // HwndSource and can't see clicks on the main visual tree).
        Loaded += (_, _) =>
        {
            _hookedWindow = Window.GetWindow(this);
            if (_hookedWindow != null)
            {
                _hookedWindow.PreviewMouseLeftButtonDown += OnWindowPreviewMouseLeftButtonDown;
            }
            _collapseGraceTimer = new DispatcherTimer { Interval = CollapseGrace };
            _collapseGraceTimer.Tick += (_, _) =>
            {
                _collapseGraceTimer.Stop();
                CollapseBar();
            };
            _idleFallbackTimer = new DispatcherTimer { Interval = IdleFallbackDelay };
            _idleFallbackTimer.Tick += (_, _) =>
            {
                if (ZoomSliderPopup.IsOpen)
                {
                    ResetIdleFallback();
                    return;
                }
                CollapseBar();
            };
            _handleColorTimer = new DispatcherTimer { Interval = HandleColorDebounce };
            _handleColorTimer.Tick += (_, _) =>
            {
                _handleColorTimer.Stop();
                EnsureHandleColorHooks();
                // MainWindow injects the viewer after the viewer's own
                // Loaded, which can land after ours — keep retrying
                // until the viewer reference shows up.
                if (_colorSubscribedViewer == null) _handleColorTimer.Start();
            };
            EnsureHandleColorHooks();
            if (_colorSubscribedViewer == null) _handleColorTimer.Start();
        };
        Unloaded += (_, _) =>
        {
            if (_hookedWindow != null)
            {
                _hookedWindow.PreviewMouseLeftButtonDown -= OnWindowPreviewMouseLeftButtonDown;
                _hookedWindow = null;
            }
            _collapseGraceTimer?.Stop();
            _collapseGraceTimer = null;
            _idleFallbackTimer?.Stop();
            _idleFallbackTimer = null;
            _handleColorTimer?.Stop();
            _handleColorTimer = null;
            UnsubscribeHandleColor();
        };
    }

    public Border FloatingBarContentRef => FloatingBarContent;

    public Border HandleRef => HandleHotZone;

    private void ZoomSliderPopup_Opened(object? sender, EventArgs e)
    {
        // Keep the bar expanded while the slider popup is open
        // (the popup anchors to the bar's zoom label).
        ExpandBar();
        if (DataContext is FloatingBarViewModel vm)
        {
            _viewer = vm.Viewer;
            if (_viewer != null)
            {
                _viewer.ZoomChanged += OnViewerZoomChanged;
                _suspendSliderUpdate = true;
                ZoomSlider.Value = Math.Round(_viewer.Zoom, 2);
                _suspendSliderUpdate = false;
            }
        }
    }

    private void ZoomSliderPopup_Closed(object? sender, EventArgs e)
    {
        if (_viewer != null)
        {
            _viewer.ZoomChanged -= OnViewerZoomChanged;
            _viewer = null;
        }
    }

    private void OnViewerZoomChanged(float zoom)
    {
        _suspendSliderUpdate = true;
        ZoomSlider.Value = Math.Round(zoom, 2);
        _suspendSliderUpdate = false;
    }

    private void ZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suspendSliderUpdate) return;
        if (DataContext is FloatingBarViewModel vm && vm.Viewer != null)
            vm.Viewer.SetZoomImmediate((float)e.NewValue);
    }

    /// <summary>Click-outside dismiss for the zoom slider popup.
    /// Popup lives in its own HwndSource so clicks inside the popup
    /// never reach the main window's preview events — we only see
    /// clicks on the main visual tree. Any such click that isn't on
    /// <see cref="ZoomPopupToggle"/> (which already toggles via its
    /// own Click handler) closes the popup by clearing
    /// <see cref="ToggleButton.IsChecked"/>; the binding propagates
    /// the change to <c>Popup.IsOpen</c>.</summary>
    private void OnWindowPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!ZoomSliderPopup.IsOpen) return;
        if (e.OriginalSource is not DependencyObject src) return;
        if (IsDescendantOf(src, ZoomPopupToggle)) return;
        // Popup is in its own HwndSource but its events bubble up the
        // logical tree to the main Window, so this handler fires for
        // clicks inside the popup too. Skip them — source's visual
        // tree is rooted at ZoomSliderPopup.Child.
        if (ZoomSliderPopup.Child is DependencyObject popupChild
            && IsDescendantOf(src, popupChild)) return;
        ZoomPopupToggle.IsChecked = false;
    }

    private static bool IsDescendantOf(DependencyObject node, DependencyObject ancestor)
    {
        var current = node;
        while (current != null)
        {
            if (current == ancestor) return true;
            current = VisualTreeHelper.GetParent(current);
        }
        return false;
    }

    // ---- Proximity expand / collapse ----

    private void OnGroupMouseEnter(object sender, MouseEventArgs e)
    {
        _collapseGraceTimer?.Stop();
        ExpandBar();
    }

    private void OnGroupMouseLeave(object sender, MouseEventArgs e)
    {
        if (!_expanded) return;
        // Grace delay so moving between the bar and the handle (or a
        // hair past an edge) doesn't flicker the bar open/closed.
        _collapseGraceTimer?.Stop();
        _collapseGraceTimer?.Start();
    }

    private void OnGroupMouseMove(object sender, MouseEventArgs e)
    {
        if (!_expanded) return;
        var pos = e.GetPosition(this);
        if (Math.Abs(pos.X - _lastMousePosition.X) > MoveResetThreshold ||
            Math.Abs(pos.Y - _lastMousePosition.Y) > MoveResetThreshold)
        {
            _lastMousePosition = pos;
            ResetIdleFallback();
        }
    }

    private void ResetIdleFallback()
    {
        _idleFallbackTimer?.Stop();
        _idleFallbackTimer?.Start();
    }

    /// <summary>Resting state: the bar is collapsed into the handle.
    /// Expanding scales the bar up out of the handle anchor point
    /// (200ms ease-out) while the handle fades away.</summary>
    private void ExpandBar()
    {
        if (_expanded)
        {
            ResetIdleFallback();
            return;
        }
        _expanded = true;
        _lastMousePosition = new Point(-10000, -10000);
        FloatingBarContent.IsHitTestVisible = true;
        AnimateBar(1d, 1d, TimeSpan.FromMilliseconds(200), EasingMode.EaseOut);
        HandlePill.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0d, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
        ResetIdleFallback();
    }

    /// <summary>Collapse the bar back onto the handle: scales down to
    /// the anchor point (250ms ease-in) while fading out, then the
    /// handle fades back in. Hit-testing is disabled so clicks pass
    /// through to the viewer. While the zoom slider popup is open the
    /// bar stays put (the popup anchors to it).</summary>
    private void CollapseBar()
    {
        if (!_expanded) return;
        if (ZoomSliderPopup.IsOpen)
        {
            ResetIdleFallback();
            return;
        }
        _expanded = false;
        FloatingBarContent.IsHitTestVisible = false;
        AnimateBar(0d, 0d, TimeSpan.FromMilliseconds(250), EasingMode.EaseIn);
        // Handle fades back in in sync with the bar's 250ms
        // collapse (EaseIn) so it emerges only once the bar has
        // mostly settled — no "handle pops out first" jump.
        HandlePill.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(1d, TimeSpan.FromMilliseconds(250))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            });
        _idleFallbackTimer?.Stop();
    }

    private void AnimateBar(double scale, double opacity, TimeSpan duration, EasingMode mode)
    {
        var ease = new CubicEase { EasingMode = mode };
        BarScale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(scale, duration) { EasingFunction = ease });
        BarScale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(scale, duration) { EasingFunction = ease });
        FloatingBarContent.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(opacity, duration) { EasingFunction = ease });
    }

    /// <summary>Collapse immediately if the pointer isn't over the
    /// bar (used when leaving fullscreen, where the bar may have been
    /// left expanded with the pointer elsewhere).</summary>
    public void CollapseIfPointerAway()
    {
        if (ZoomSliderPopup.IsOpen) ZoomPopupToggle.IsChecked = false;
        if (_expanded && !IsMouseOver) CollapseBar();
    }

    // ---- Handle color adaptation ----

    private void EnsureHandleColorHooks()
    {
        if (DataContext is not FloatingBarViewModel vm || vm.Viewer is not { } viewer) return;
        if (!ReferenceEquals(_colorSubscribedViewer, viewer))
        {
            UnsubscribeHandleColor();
            _colorSubscribedViewer = viewer;
            viewer.ImageLoaded += OnViewerImageLoadedForHandle;
            viewer.ZoomChanged += OnViewerZoomChangedForHandle;
        }
        UpdateHandleColor();
    }

    private void UnsubscribeHandleColor()
    {
        if (_colorSubscribedViewer != null)
        {
            _colorSubscribedViewer.ImageLoaded -= OnViewerImageLoadedForHandle;
            _colorSubscribedViewer.ZoomChanged -= OnViewerZoomChangedForHandle;
            _colorSubscribedViewer = null;
        }
    }

    private void OnViewerImageLoadedForHandle(ImageLoadResult _) => UpdateHandleColor();

    private void OnViewerZoomChangedForHandle(float _)
    {
        // Zoom / pan / rotate animations fire per-frame; debounce so
        // we sample once the transform has settled.
        _handleColorTimer?.Stop();
        _handleColorTimer?.Start();
    }

    private void UpdateHandleColor()
    {
        if (DataContext is not FloatingBarViewModel vm || vm.Viewer is not { } viewer) return;
        // TranslatePoint maps the pill's center into the viewer's
        // coordinate space, so the sample follows the pill wherever
        // the window layout puts it.
        var center = HandlePill.TranslatePoint(
            new Point(HandlePill.ActualWidth / 2, HandlePill.ActualHeight / 2), viewer);
        if (viewer.TryGetLuminanceAt(center.X, center.Y, 2, out var luminance))
        {
            HandlePill.Background = luminance > HandleLuminanceThreshold ? DarkHandleBrush : LightHandleBrush;
            return;
        }
        // No image under the pill (letterboxed border / empty viewer):
        // the letterbox shows the window background, so contrast
        // against THAT instead of assuming dark — a light pill on the
        // light theme's white letterbox would be invisible.
        HandlePill.Background = WindowBackgroundIsDark ? LightHandleBrush : DarkHandleBrush;
    }

    private static bool WindowBackgroundIsDark
    {
        get
        {
            var brush = Application.Current?.MainWindow?.Background as SolidColorBrush;
            brush ??= Application.Current?.Resources["SurfaceCanvas"] as SolidColorBrush;
            var c = brush?.Color ?? Colors.White;
            return (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0 < HandleLuminanceThreshold;
        }
    }
}
