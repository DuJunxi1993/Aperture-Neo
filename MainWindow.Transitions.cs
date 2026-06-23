using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ApertureNeo.Helpers;

namespace ApertureNeo;

/// <summary>
/// Window-level visual transitions: fullscreen state machine
/// (toggle / enter / exit / cross-fade background / WPF-UI
/// padding reset), fullscreen-only UI animations (edge-nav
/// show/hide on mouse-move + 3s hide timer, exit-pill slide +
/// fade), and the tree floating popup (hot-zone MouseEnter +
/// 250ms MouseLeave hide timer).
///
/// P2 step 9: this was previously split across three
/// controller partials (FullscreenController, EdgeNavController,
/// TreeController) under Controllers/MainWindowControllers/.
/// Those controllers are deleted; their remaining live methods
/// are consolidated here for readability.
///
/// These stay in MainWindow because they manipulate the
/// window's visual tree directly (animations on
/// EdgeNavLeftContent, ExitFullscreenHint, ViewerColumn,
/// TreeFloatingPopup). The VMs that observe IUiState
/// properties don't have access to these elements.
/// </summary>
public partial class MainWindow
{
    // ---- Fullscreen state machine ----

    // P1 fix: transition generation. The 150ms fade-out's
    // Completed callback is async (runs on the next dispatcher
    // frame). If the user toggles fullscreen twice within 150ms the
    // first callback still fires after the second transition has
    // already mutated _isFullscreen, swapping WindowState +
    // ApplyColumnVisibility + UpdateLayout a second time. The
    // generation counter lets the callback ignore itself when a
    // newer transition has started.
    private int _transitionGeneration;

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
        // P3: ITheme replaces FindResource("SurfaceBlack"/"SurfaceElevated").
        // The fullscreen transition animation needs the brushes
        // by value (the background ColorAnimation tween reads the
        // brush's colour), so we resolve once here and pass the
        // frozen reference into AnimateViewerBackground.
        var targetBackground = entering
            ? _theme.SurfaceBlack
            : _theme.SurfaceElevated;

        // P1 fix: stamp this transition with a generation number. If
        // the user toggles fullscreen again before this fadeOut's
        // Completed callback fires, the older callback's generation
        // will be stale and the callback will return without
        // mutating WindowState / ApplyColumnVisibility. The newer
        // transition's callbacks run unimpeded.
        var myGen = ++_transitionGeneration;

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
            // Bail if a newer TransitionToFullscreen has started
            // since this one. Without this guard, a rapid
            // Ctrl+F, Ctrl+F would re-apply the second transition's
            // OS swap a second time, fighting the new fade.
            if (myGen != _transitionGeneration) return;


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
                // P1 fix: show both the exit hint AND the edge
                // nav on fullscreen entry (and keep them
                // visible for the entire session). The previous
                // behavior collapsed the edge nav to invisible
                // and only revealed it on the first mouse
                // move — which left a user who tapped Ctrl+F
                // and then sat still with no prev/next
                // affordance. Now both are visible from t=0.
                ShowEdgeNav();
                // Show the exit hint. The hint used to auto-
                // hide after 3s, but that left the user with
                // no "how do I exit?" affordance once it
                // faded away. We now keep it persistent for
                // the entire fullscreen session — the only
                // time it disappears is on fullscreen exit
                // (HideExitFullscreenHint in the exit branch).
                // Stop the auto-hide timer (legacy wiring
                // kept around for clean shutdown in the
                // Closed handler) so it doesn't fire and hide
                // the hint out from under us.
                ShowExitFullscreenHint();
                _exitHintHideTimer?.Stop();
                _overlayHideTimer?.Stop();
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
            // P1 fix: seed the new brush with the theme's resting
            // color instead of Colors.White. The previous fallback
            // worked only because SurfaceElevated happened to be
            // white; if the design token ever changes, the first
            // cross-fade would visibly flash from white to the new
            // color.
            var seed = current?.Color
                ?? ((System.Windows.Media.SolidColorBrush)_theme.SurfaceElevated).Color;
            current = new System.Windows.Media.SolidColorBrush(seed);
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

    // ---- Edge nav (fullscreen-only) ----

    private void OnWindowMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isFullscreen) return;
        var pos = e.GetPosition(this);
        if (Math.Abs(pos.X - _lastMousePosition.X) > 5 || Math.Abs(pos.Y - _lastMousePosition.Y) > 5)
        {
            _lastMousePosition = pos;
            // P1 fix: edge nav is now persistent in fullscreen
            // (it shows on the first mouse move after entry
            // and stays visible until the user exits
            // fullscreen). The previous auto-hide timer left
            // the user with no nav affordance 3s after every
            // mouse-stop. We still no-op if it's already
            // visible so the no-op branch doesn't re-trigger
            // the fade-in on every micro-movement.
            ShowEdgeNav();
        }
    }

    /// <summary>
    /// Show the fullscreen edge-nav buttons (left + right). Cancels any
    /// in-flight fade-out, makes the UserControls Visible, and animates
    /// their Opacity 0→1 over 200ms. Subsequent calls while already
    /// visible are a no-op — the buttons stay visible until fullscreen
    /// exits (no auto-hide, since the user needs persistent navigation
    /// affordance in fullscreen).
    ///
    /// P1 fix: animates the UserControl's Opacity, not the inner
    /// Border's. The UserControl has Opacity=0 in XAML; the WPF
    /// compositor multiplies parent×child Opacity, so animating the
    /// child Border was a no-op (the parent's 0 won). See the
    /// EdgeNavLeftControl property for the full explanation.
    /// </summary>
    private void ShowEdgeNav()
    {
        if (_edgeNavVisible) return;
        _edgeNavVisible = true;
        EdgeNavLeftControl.BeginAnimation(UIElement.OpacityProperty, null);
        EdgeNavRightControl.BeginAnimation(UIElement.OpacityProperty, null);
        EdgeNavLeftControl.Visibility = Visibility.Visible;
        EdgeNavRightControl.Visibility = Visibility.Visible;
        var fadeIn = new DoubleAnimation(0d, 1d, TimeSpan.FromMilliseconds(200));
        EdgeNavLeftControl.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        EdgeNavRightControl.BeginAnimation(UIElement.OpacityProperty, fadeIn);
    }

    /// <summary>
    /// Fade the edge-nav buttons out (200ms) and collapse them once the
    /// animation completes. Safe to call when already hidden.
    ///
    /// P1 fix: animates the UserControl's Opacity, not the inner
    /// Border's — see ShowEdgeNav for the full reasoning.
    /// </summary>
    private void HideEdgeNav()
    {
        if (!_edgeNavVisible) return;
        _edgeNavVisible = false;
        var fadeOut = new DoubleAnimation(1d, 0d, TimeSpan.FromMilliseconds(200));
        fadeOut.Completed += (_, _) =>
        {
            if (_edgeNavVisible) return; // re-shown mid-fade; leave it
            EdgeNavLeftControl.Visibility = Visibility.Collapsed;
            EdgeNavRightControl.Visibility = Visibility.Collapsed;
            EdgeNavLeftControl.Opacity = 0;
            EdgeNavRightControl.Opacity = 0;
        };
        EdgeNavLeftControl.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        EdgeNavRightControl.BeginAnimation(UIElement.OpacityProperty, fadeOut);
    }

    private void ResetOverlayHideTimer()
    {
        // No-op stub kept for binary compat: the auto-hide
        // timer was removed in the P1 fix that made the
        // exit hint + edge nav persistent in fullscreen.
        // ResetOverlayHideTimer was previously called from
        // OnWindowMouseMove and ShowEdgeNav, both of which
        // no longer restart the timer.
    }

    private void ShowExitFullscreenHint()
    {
        // P1 fix: animate the UserControl's Opacity, not the
        // inner Border's. The XAML declares the
        // ExitFullscreenHintView with Opacity="0", and WPF
        // composites parent×child Opacity — so animating the
        // child Border (the old ExitFullscreenHint property)
        // was a no-op. The TranslateTransform stays on the
        // Border (the slide animation reads the actual
        // element's position) — that's separate from the
        // visibility problem.
        ExitFullscreenHintControl.Visibility = Visibility.Visible;
        ExitFullscreenHintControl.BeginAnimation(UIElement.OpacityProperty, null);
        ExitFullscreenTransform.BeginAnimation(TranslateTransform.YProperty, null);
        var fadeIn = new DoubleAnimation(0d, 1d, TimeSpan.FromMilliseconds(200));
        var slideIn = new DoubleAnimation(-50d, 0d, TimeSpan.FromMilliseconds(200))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        ExitFullscreenHintControl.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        ExitFullscreenTransform.BeginAnimation(TranslateTransform.YProperty, slideIn);
    }

    private void HideExitFullscreenHint()
    {
        // P1 fix: animate the UserControl, not the Border
        // (see ShowExitFullscreenHint for the full reasoning).
        ExitFullscreenHintControl.BeginAnimation(UIElement.OpacityProperty, null);
        ExitFullscreenTransform.BeginAnimation(TranslateTransform.YProperty, null);
        var fadeOut = new DoubleAnimation(1d, 0d, TimeSpan.FromMilliseconds(200));
        var slideOut = new DoubleAnimation(0d, -50d, TimeSpan.FromMilliseconds(200))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        ExitFullscreenHintControl.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        ExitFullscreenTransform.BeginAnimation(TranslateTransform.YProperty, slideOut);
    }

    // ---- Floating tree popup (hot zone + panel) ----

    private DispatcherTimer? _treeHideTimer;

    /// <summary>
    /// Mouse entered the 8px left-edge hot zone. Show the floating
    /// tree popup if the inline tree is currently collapsed. The
    /// popup mirrors the inline tree's items and selected node via
    /// SyncFrom, so the visual state is consistent on first show.
    /// </summary>
    private void TreeHotZone_MouseEnter(object sender, MouseEventArgs e)
    {
        if (_isFullscreen || _isTreeVisible) return;
        _treeHideTimer?.Stop();
        if (!TreeFloatingPopup.IsOpen)
        {
            FolderTreeFloating.SyncFrom(FolderTree);
            TreeFloatingPopup.HorizontalOffset = 0;
            TreeFloatingPopup.VerticalOffset = 0;
            TreeFloatingPopup.IsOpen = true;
        }
    }

    private void TreeHotZone_MouseLeave(object sender, MouseEventArgs e)
    {
        // Don't close the popup yet — the user is probably moving
        // their cursor INTO the floating tree. The popup's own
        // MouseLeave handler will fire when the cursor truly exits
        // both the hot zone and the popup, at which point we close.
    }

    private void TreeFloatingPanel_MouseEnter(object sender, MouseEventArgs e)
    {
        _treeHideTimer?.Stop();
    }

    /// <summary>
    /// Mouse left the floating panel. Start a short hide timer so
    /// the panel doesn't close while the cursor is crossing the
    /// 4px gap between the popup edge and the next target. If the
    /// cursor re-enters within 250ms (via hot zone or panel), the
    /// timer is cancelled.
    /// </summary>
    private void TreeFloatingPanel_MouseLeave(object sender, MouseEventArgs e)
    {
        if (_treeHideTimer == null)
        {
            _treeHideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _treeHideTimer.Tick += (_, _) =>
            {
                _treeHideTimer.Stop();
                TreeFloatingPopup.IsOpen = false;
            };
        }
        _treeHideTimer.Stop();
        _treeHideTimer.Start();
    }

    private void TreeFloatingPopup_Closed(object? sender, EventArgs e)
    {
        // The popup closed (either via the hide timer or by the
        // user collapsing the tree). Nothing to do beyond a
        // defensive timer stop.
        _treeHideTimer?.Stop();
    }
}