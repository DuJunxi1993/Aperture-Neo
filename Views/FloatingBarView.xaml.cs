using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using ApertureNeo.Controls;
using ApertureNeo.ViewModels;

namespace ApertureNeo.Views;

public partial class FloatingBarView : UserControl
{
    private bool _suspendSliderUpdate;
    private SkiaImageViewer? _viewer;
    private Window? _hookedWindow;

    public FloatingBarView()
    {
        InitializeComponent();
        DataContext = AppHost.Services?.GetService<FloatingBarViewModel>();

        // Hook Window-level mouse once Loaded so we can detect
        // clicks outside the popup and dismiss it. The handler is
        // a no-op while the popup is closed.
        Loaded += (_, _) =>
        {
            _hookedWindow = Window.GetWindow(this);
            _hookedWindow?.PreviewMouseLeftButtonDown += OnWindowPreviewMouseLeftButtonDown;
        };
        Unloaded += (_, _) =>
        {
            _hookedWindow?.PreviewMouseLeftButtonDown -= OnWindowPreviewMouseLeftButtonDown;
            _hookedWindow = null;
        };
    }

    public Border FloatingBarContentRef => FloatingBarContent;

    private void ZoomSliderPopup_Opened(object? sender, EventArgs e)
    {
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
}