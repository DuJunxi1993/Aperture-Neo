using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ApertureNeo.Helpers;

namespace ApertureNeo;

/// <summary>
/// Fullscreen state machine: the toggle + enter/exit transitions
/// (shared via TransitionToFullscreen), the visual background
/// cross-fade, the WPF-UI ClientAreaBorder padding reset, and
/// the double-click Fit↔100% toggle. Extracted from
/// MainWindow.xaml.cs as a partial class.
/// </summary>
public partial class MainWindow
{
    private void ToggleFullscreen()
    {
        _isFullscreen = !_isFullscreen;
        if (_isFullscreen) EnterFullscreen();
        else               ExitFullscreen();
    }

    /// <summary>
    /// Enter fullscreen with the simplest possible animation: hide the
    /// viewer area synchronously (Opacity=0, no animation), change the
    /// OS window state, apply the chrome, refit the image synchronously,
    /// then fade the viewer back in over 150ms. The viewer background
    /// does a parallel 150ms ColorAnimation white→black. That's it —
    /// no flash overlays, no delayed FitToScreen, no RenderTransform
    /// scaling. The single Opacity fade hides every layout recompute
    /// and ClientAreaBorder padding change that would otherwise show
    /// as a flicker.
    /// </summary>
    private void EnterFullscreen()
    {
        _prevWindowState = WindowState;
        TransitionToFullscreen(entering: true);
    }

    /// <summary>
    /// Exit fullscreen: fade out the viewer + restore the OS window
    /// in the Completed callback + fade back in. Viewer background
    /// does a parallel black→white animation.
    /// </summary>
    private void ExitFullscreen()
    {
        // Pre-fade hint cleanup. Doing this BEFORE the fadeOut
        // animation (rather than inside the Completed callback) so
        // the exit pill starts its own fade-out in parallel with the
        // viewer's — the user perceives a single coordinated exit.
        HideExitFullscreenHint();
        _exitHintHideTimer?.Stop();

        TransitionToFullscreen(entering: false);
    }

    /// <summary>
    /// Shared fullscreen transition. Mirrors the enter/exit pair so
    /// they stay symmetric: a 150ms ease-in fade-out hides the viewer
    /// (and any layout recompute) from the user, then the OS-level
    /// WindowState/WindowStyle swap + chrome + UpdateLayout +
    /// FitToScreen happens in the Completed callback while the
    /// viewer is still invisible, then a 150ms ease-out fade-in
    /// reveals the new layout. A 150ms background ColorAnimation
    /// runs in parallel so the background is mid-transition when the
    /// fade-in completes. Total 300ms either direction.
    /// </summary>
    private void TransitionToFullscreen(bool entering)
    {
        var targetBackground = entering
            ? (System.Windows.Media.Brush)FindResource("SurfaceBlack")
            : (System.Windows.Media.Brush)FindResource("SurfaceElevated");

        // 1. Fade the viewer to 0 over 150ms (ease-in). After this
        //    completes, swap the OS-level state without the user
        //    seeing any layout recompute.
        var fadeOut = new System.Windows.Media.Animation.DoubleAnimation(
            0d, TimeSpan.FromMilliseconds(150))
        {
            EasingFunction = new System.Windows.Media.Animation.CubicEase
            {
                EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn
            }
        };
        fadeOut.Completed += (_, _) =>
        {
            // 2. OS-level swap. Entering becomes borderless + covers
            //    the whole screen. Exiting forces Normal rather than
            //    _prevWindowState so a previous Maximized state
            //    doesn't accidentally no-op (since the window is
            //    currently Maximized).
            if (entering)
            {
                WindowStyle = WindowStyle.None;
                WindowState = WindowState.Maximized;
            }
            else
            {
                WindowStyle = WindowStyle.SingleBorderWindow;
                WindowState = WindowState.Normal;
            }

            // 3. Apply chrome state synchronously. ApplyColumnVisibility
            //    collapses the side columns; ResetClientAreaBorderPadding
            //    kills WPF-UI's 5px-maximized padding.
            ApplyColumnVisibility();
            UpdateOverlayVisibility();
            ResetClientAreaBorderPadding();

            // 4. Force a synchronous layout pass. WPF layout is async
            //    after WindowState change — without UpdateLayout the
            //    SkiaImageViewer's ActualWidth/ActualHeight still
            //    reflect the pre-transition size, so FitToScreen
            //    would compute centering offsets for the old rect.
            UpdateLayout();

            // 5. Snap the image to the new fit immediately. The viewer
            //    is still at Opacity=0 (we haven't started the
            //    fade-in yet), so the snap is invisible. Skip the
            //    zoom animation because the 200ms slide-in would be
            //    visible through the following 150ms opacity fade.
            ImageViewer.FitToScreenSkipAnimation = true;
            ImageViewer.FitToScreen();

            if (entering)
            {
                // Hide the edge nav and show the exit hint.
                _edgeNavVisible = false;
                EdgeNavLeftContent.Visibility = Visibility.Collapsed;
                EdgeNavRightContent.Visibility = Visibility.Collapsed;
                EdgeNavLeftContent.Opacity = 0;
                EdgeNavRightContent.Opacity = 0;
                _overlayHideTimer?.Stop();
                ShowExitFullscreenHint();
                _exitHintHideTimer?.Stop();
                _exitHintHideTimer?.Start();
            }
            else
            {
                // Re-focus the window so Esc / arrow keys reach the
                // keyboard handler after the HWND swap.
                Focus();

                // If the user had the window Maximized before fullscreen,
                // re-maximize it on the next dispatcher cycle (deferred
                // so it doesn't fight the WindowStyle HWND recreation
                // happening above).
                if (_prevWindowState == WindowState.Maximized)
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        WindowState = WindowState.Maximized;
                    }), System.Windows.Threading.DispatcherPriority.Background);
                }
            }

            // 6. Fade the viewer back in over 150ms (ease-out). The
            //    image appears in its new fit at the new size, on a
            //    background that's mid-transition.
            ViewerColumn.BeginAnimation(UIElement.OpacityProperty,
                new System.Windows.Media.Animation.DoubleAnimation(1d, TimeSpan.FromMilliseconds(150))
                {
                    EasingFunction = new System.Windows.Media.Animation.CubicEase
                    {
                        EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut
                    }
                });
        };
        ViewerColumn.BeginAnimation(UIElement.OpacityProperty, fadeOut);

        // 7. Parallel: viewer background color cross-fade. Started
        //    alongside the fade-out so the background is mid-
        //    transition when the fade-in reveals the new view.
        AnimateViewerBackground(targetBackground, 150);
    }

    /// <summary>
    /// Walks the visual tree looking for a FluentWindow.ClientAreaBorder
    /// (an internal WPF-UI class) and zeroes its Padding. WPF-UI's
    /// OnWindowStateChanged sets Padding to ~5px in Maximized state to
    /// keep the OS's "Aero" border visible — but our viewer is edge-to-
    /// edge, so the ~5px of transparent padding shows the window's
    /// SurfaceCanvas (#fafafa) tone around the white viewer, producing
    /// a 1-2px white-ish seam at every screen edge. We can't reference
    /// the type by name (it's internal), so we match on the class name
    /// in the visual tree and set the public Padding DP inherited from
    /// Border. This is invoked both synchronously inside ToggleFullscreen
    /// (so it beats the FluentWindow padding on the same dispatcher turn)
    /// and on DispatcherPriority.Loaded (catches the case where the
    /// FluentWindow sets padding after we do).
    /// </summary>
    private void ResetClientAreaBorderPadding()
    {
        var cab = VisualTreeHelpers.FindClientAreaBorder(this);
        if (cab == null) return;
        cab.SetValue(System.Windows.Controls.Border.PaddingProperty, new Thickness(0));
    }

    /// <summary>
    /// Goal 3: cross-fade the viewer column background over 200ms.
    /// SolidColorBrush is mutated via ColorAnimation; the brush instance
    /// is kept (so the rest of the visual tree that referenced it
    /// stays valid) and only the underlying color animates.
    /// </summary>
    private void AnimateViewerBackground(System.Windows.Media.Brush target, double durationMs = 200)
    {
        if (target is not System.Windows.Media.SolidColorBrush targetSolid) return;
        // Clone the resource brush so we own the color (resource brushes
        // are shared/frozen; we can't mutate them).
        var current = ViewerColumn.Background as System.Windows.Media.SolidColorBrush;
        if (current == null || current.IsFrozen)
        {
            current = new System.Windows.Media.SolidColorBrush(
                current?.Color ?? System.Windows.Media.Colors.White);
        }
        else
        {
            // Detach the previous animation so the new one wins.
            current.BeginAnimation(System.Windows.Media.SolidColorBrush.ColorProperty, null);
        }
        ViewerColumn.Background = current;
        var anim = new System.Windows.Media.Animation.ColorAnimation
        {
            From = current.Color,
            To = targetSolid.Color,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            EasingFunction = new System.Windows.Media.Animation.CubicEase
            {
                EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut
            }
        };
        current.BeginAnimation(System.Windows.Media.SolidColorBrush.ColorProperty, anim);
    }

    /// <summary>
    /// Double-click on the viewer (outside buttons): toggle the
    /// image between Fit-to-screen and 100% (same as the
    /// floating-bar percent label click semantics, but
    /// round-tripping in both directions). Behaviour is the same
    /// in window mode and fullscreen mode — double-click never
    /// exits fullscreen anymore. Esc / Ctrl+F still does.
    /// Preview (tunneling) phase + e.Handled=true so the event
    /// does not bubble to underlying controls (the edge-nav
    /// arrows in fullscreen, the floating bar in window mode).
    /// </summary>
    private void Viewer_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ClickCount < 2) return;
        // Don't trigger on the edge-nav buttons, the exit pill, or
        // the floating bar — those have their own click semantics.
        if (e.OriginalSource is DependencyObject src)
        {
            DependencyObject? walker = src;
            while (walker != null && walker != this)
            {
                if (walker is System.Windows.Controls.Button)
                    return;
                if (walker is System.Windows.Controls.Primitives.ButtonBase)
                    return;
                walker = System.Windows.Media.VisualTreeHelper.GetParent(walker);
            }
        }
        // Toggle between Fit and 100%, in both window mode and
        // fullscreen mode. (Matches the floating-bar percent label
        // click semantics, but in both directions — clicking the
        // percent always zooms to 100%, the viewer double-click
        // rounds-trips fit↔100%.)
        if (ImageViewer.IsAtFitScale)
            ImageViewer.ZoomToOriginal();
        else
            ImageViewer.FitToScreen();
        e.Handled = true;
    }

    private void ZoomTextBlock_Click(object sender, MouseButtonEventArgs e) => ImageViewer.ZoomToOriginal();
}
