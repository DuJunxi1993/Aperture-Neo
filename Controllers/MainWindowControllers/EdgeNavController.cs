using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ApertureNeo;

/// <summary>
/// Fullscreen-only UI concerns: the edge-nav arrows (left/right
/// for prev/next) and the exit-pill at the top. Both fade in
/// during fullscreen when the user moves the mouse, then auto-
/// hide after a few seconds of inactivity. Extracted from
/// MainWindow.xaml.cs as a partial class for readability — the
/// show/hide methods are self-contained and don't need any
/// state outside the fields the partial class already shares.
/// </summary>
public partial class MainWindow
{
    private void OnWindowMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isFullscreen) return;
        var pos = e.GetPosition(this);
        if (Math.Abs(pos.X - _lastMousePosition.X) > 5 || Math.Abs(pos.Y - _lastMousePosition.Y) > 5)
        {
            _lastMousePosition = pos;
            // Goal 1: if the cursor is hovering over an edge-nav button,
            // do NOT re-arm the auto-hide timer. The previous MouseEnter
            // hook missed the case where the cursor stays *inside* the
            // button (Border.MouseEnter only fires when the cursor
            // crosses the Border edge, not when it stays inside a
            // child Button). IsMouseOver stays true the entire time
            // the cursor is anywhere inside the border.
            bool cursorOverEdgeNav =
                EdgeNavLeftContent.IsMouseOver || EdgeNavRightContent.IsMouseOver;
            if (cursorOverEdgeNav)
            {
                EdgeNavLeftContent.BeginAnimation(UIElement.OpacityProperty, null);
                EdgeNavRightContent.BeginAnimation(UIElement.OpacityProperty, null);
                EdgeNavLeftContent.Opacity = 1;
                EdgeNavRightContent.Opacity = 1;
                _edgeNavVisible = true;
                _overlayHideTimer?.Stop();
            }
            else
            {
                ShowEdgeNav();
                ResetOverlayHideTimer();
            }
            // Exit-pill is no longer triggered by cursor position; it
            // appears on fullscreen entry and auto-hides after 3s (see
            // _exitHintHideTimer wiring in the constructor).
        }
    }

    /// <summary>
    /// Goal 2: slide/fade the exit-fullscreen pill in when the cursor
    /// enters the top 300 DIP strip, and back out when it leaves. The
    /// pill is fullscreen-only; we no-op otherwise. Uses a strict
    /// 300px trigger; no edge cases (Y must be > 0 and &lt; 300).
    /// </summary>
    private void UpdateExitFullscreenHint(double y)
    {
        if (!_isFullscreen) return;
        const double triggerZone = 300.0;
        bool shouldShow = y > 0 && y < triggerZone;
        if (shouldShow && ExitFullscreenHint.Visibility != Visibility.Visible)
            ShowExitFullscreenHint();
        else if (!shouldShow && ExitFullscreenHint.Visibility == Visibility.Visible)
            HideExitFullscreenHint();
    }

    private void ShowExitFullscreenHint()
    {
        ExitFullscreenHint.Visibility = Visibility.Visible;
        ExitFullscreenHint.BeginAnimation(UIElement.OpacityProperty, null);
        ExitFullscreenTransform.BeginAnimation(TranslateTransform.YProperty, null);
        var fadeIn = new DoubleAnimation(0d, 1d, TimeSpan.FromMilliseconds(200));
        var slideIn = new DoubleAnimation(-50d, 0d, TimeSpan.FromMilliseconds(200))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        ExitFullscreenHint.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        ExitFullscreenTransform.BeginAnimation(TranslateTransform.YProperty, slideIn);
    }

    private void HideExitFullscreenHint()
    {
        ExitFullscreenHint.BeginAnimation(UIElement.OpacityProperty, null);
        ExitFullscreenTransform.BeginAnimation(TranslateTransform.YProperty, null);
        var fadeOut = new DoubleAnimation(1d, 0d, TimeSpan.FromMilliseconds(200));
        var slideOut = new DoubleAnimation(0d, -50d, TimeSpan.FromMilliseconds(200))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        ExitFullscreenHint.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        ExitFullscreenTransform.BeginAnimation(TranslateTransform.YProperty, slideOut);
    }

    /// <summary>
    /// Show the fullscreen edge-nav buttons (left + right). Cancels any
    /// in-flight fade-out, makes the borders Visible, and animates
    /// Opacity 0→1 over 200ms. Subsequent calls while already visible
    /// are a no-op — the timer restart is the only side-effect of
    /// repeated mouse-move events.
    /// </summary>
    private void ShowEdgeNav()
    {
        if (_edgeNavVisible) { ResetOverlayHideTimer(); return; }
        _edgeNavVisible = true;
        EdgeNavLeftContent.BeginAnimation(UIElement.OpacityProperty, null);
        EdgeNavRightContent.BeginAnimation(UIElement.OpacityProperty, null);
        EdgeNavLeftContent.Visibility = Visibility.Visible;
        EdgeNavRightContent.Visibility = Visibility.Visible;
        var fadeIn = new DoubleAnimation(0d, 1d, TimeSpan.FromMilliseconds(200));
        EdgeNavLeftContent.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        EdgeNavRightContent.BeginAnimation(UIElement.OpacityProperty, fadeIn);
    }

    /// <summary>
    /// Fade the edge-nav buttons out (200ms) and collapse them once the
    /// animation completes. Safe to call when already hidden.
    /// </summary>
    private void HideEdgeNav()
    {
        if (!_edgeNavVisible) return;
        _edgeNavVisible = false;
        var fadeOut = new DoubleAnimation(1d, 0d, TimeSpan.FromMilliseconds(200));
        fadeOut.Completed += (_, _) =>
        {
            if (_edgeNavVisible) return; // re-shown mid-fade; leave it
            EdgeNavLeftContent.Visibility = Visibility.Collapsed;
            EdgeNavRightContent.Visibility = Visibility.Collapsed;
            EdgeNavLeftContent.Opacity = 0;
            EdgeNavRightContent.Opacity = 0;
        };
        EdgeNavLeftContent.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        EdgeNavRightContent.BeginAnimation(UIElement.OpacityProperty, fadeOut);
    }

    private void ResetOverlayHideTimer()
    {
        _overlayHideTimer?.Stop();
        _overlayHideTimer?.Start();
    }
}
