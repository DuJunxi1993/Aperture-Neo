using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ApertureNeo.Helpers;

namespace ApertureNeo.Services;

/// <summary>
/// Fullscreen state machine + animations. See
/// <see cref="IFullscreenController"/> for the role this
/// plays. The implementation is a straight port of the
/// logic that previously lived in MainWindow.Transitions
/// (toggle / enter / exit / cross-fade / WPF-UI padding
/// reset / edge-nav show-hide / exit-pill slide-fade / tree
/// floating popup).
///
/// All visual manipulation goes through the
/// <see cref="FullscreenShell"/> — the controller itself
/// is XAML-free and unit-testable: in tests, pass a shell
/// with stub actions + a real Grid (or a mock) and verify
/// the timer / state machine behaviour.
/// </summary>
public class FullscreenController : IFullscreenController
{
    private readonly ITheme _theme;
    private readonly FullscreenShell _shell;

    // ---- Fullscreen state ----

    private bool _isFullscreen;

    /// <summary>Counted every Toggle. The 150ms fade-out's
    /// Completed callback captures the gen at trigger time
    /// and bails if a newer Toggle has happened — without
    /// this, rapid Ctrl+F, Ctrl+F would re-apply the second
    /// transition's OS swap + chrome + FitToScreen a second
    /// time, fighting the new fade.</summary>
    private int _transitionGeneration;

    /// <summary>True while edge-nav buttons are mid-fade or
    /// fully shown. Suppresses re-triggering the show
    /// animation on every micro mouse-move event (otherwise
    /// the Opacity would fight the timer restart
    /// constantly).</summary>
    private bool _edgeNavVisible;

    /// <summary>Last mouse position. <see cref="OnMouseMove"/>
    /// no-ops unless the cursor has moved more than 5px from
    /// here, which prevents the overlay from re-showing on
    /// pixel-level mouse jitter.</summary>
    private Point _lastMousePosition;

    // ---- Timers (all dispatcher-timers, fire on the UI thread) ----

    /// <summary>3s timer that hides the edge-nav buttons
    /// when the user stops moving the mouse.</summary>
    private DispatcherTimer? _overlayHideTimer;

    /// <summary>5s timer that hides the exit-fullscreen hint
    /// shortly after entering fullscreen.</summary>
    private DispatcherTimer? _exitHintHideTimer;

    /// <summary>250ms timer that closes the tree floating
    /// popup after the cursor leaves it.</summary>
    private DispatcherTimer? _treeHideTimer;

    public FullscreenController(ITheme theme, FullscreenShell shell)
    {
        _theme = theme;
        _shell = shell;

        _overlayHideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.0) };
        _overlayHideTimer.Tick += (_, _) =>
        {
            if (_isFullscreen) HideEdgeNav();
            _overlayHideTimer?.Stop();
        };

        _exitHintHideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5.0) };
        _exitHintHideTimer.Tick += (_, _) =>
        {
            if (_isFullscreen) HideExitFullscreenHint();
            _exitHintHideTimer.Stop();
        };
    }

    public bool IsFullscreen => _isFullscreen;

    // ---- Fullscreen state machine ----

    public void Toggle()
    {
        // Capture the pre-toggle WindowState ONLY when entering
        // fullscreen. Calling Toggle on the way OUT must not
        // re-capture: by then WindowState is already Maximized
        // (set by the entering transition itself), which the
        // exit transition's "restore Maximized if user was
        // Maximized" path would then misread as "user was
        // Maximized before fullscreen" and re-maximize a
        // window that was actually Normal. P2 fix: move the
        // capture inside Toggle so the two-method
        // NotifyEnteringFullscreen + Toggle protocol can't
        // be misused by future call sites.
        if (!_isFullscreen)
        {
            _previousWindowState = _shell.Window.WindowState == WindowState.Maximized
                ? WindowState.Maximized
                : WindowState.Normal;
        }

        _isFullscreen = !_isFullscreen;
        if (_isFullscreen) Enter();
        else               Exit();
    }

    private void Enter()
    {
        Transition(entering: true);
    }

    private void Exit()
    {
        // Pre-fade hint cleanup. Doing this BEFORE the fadeOut
        // animation (rather than inside the Completed callback) so
        // the exit pill starts its own fade-out in parallel with
        // the viewer's — the user perceives a single coordinated exit.
        HideExitFullscreenHint();
        _exitHintHideTimer?.Stop();
        Transition(entering: false);
    }

    /// <summary>
    /// Shared fullscreen transition. Mirrors the enter/exit
    /// pair so they stay symmetric: a 150ms ease-in fade-out
    /// hides the viewer (and any layout recompute) from the
    /// user, then the OS-level WindowState/WindowStyle swap +
    /// chrome + UpdateLayout + FitToScreen happens in the
    /// Completed callback while the viewer is still
    /// invisible, then a 150ms ease-out fade-in reveals the
    /// new layout. A 150ms background ColorAnimation runs in
    /// parallel so the background is mid-transition when the
    /// fade-in completes. Total 300ms either direction.
    /// </summary>
    private void Transition(bool entering)
    {
        // The fullscreen transition animation needs the
        // brushes by value (the background ColorAnimation
        // tween reads the brush's colour), so we resolve once
        // here and pass the frozen reference into
        // AnimateViewerBackground.
        var targetBackground = entering
            ? _theme.SurfaceBlack
            : _theme.SurfaceElevated;

        // Stamp this transition with a generation number. If
        // the user toggles fullscreen again before this
        // fadeOut's Completed callback fires, the older
        // callback's generation will be stale and the
        // callback will return without muting WindowState /
        // ApplyColumnVisibility. The newer transition's
        // callbacks run unimpeded.
        var myGen = ++_transitionGeneration;

        // 1. Fade the viewer to 0 over 150ms (ease-in). After
        //    this completes, swap the OS-level state without
        //    the user seeing any layout recompute.
        var fadeOut = new DoubleAnimation(0d, TimeSpan.FromMilliseconds(150))
        {
            EasingFunction = new CubicEase
            {
                EasingMode = EasingMode.EaseIn
            }
        };
        fadeOut.Completed += (_, _) =>
        {
            if (myGen != _transitionGeneration) return;

            // 2. OS-level swap.
            if (entering)
            {
                _shell.Window.WindowStyle = WindowStyle.None;
                _shell.Window.WindowState = WindowState.Maximized;
            }
            else
            {
                _shell.Window.WindowStyle = WindowStyle.SingleBorderWindow;
                _shell.Window.WindowState = WindowState.Normal;
            }

            // 3. Apply chrome state synchronously.
            _shell.ApplyChrome();
            _shell.ResetClientAreaBorderPadding();

            // 4. Force a synchronous layout pass. WPF layout
            //    is async after WindowState change — without
            //    UpdateLayout the SkiaImageViewer's
            //    ActualWidth/ActualHeight still reflect the
            //    pre-transition size, so FitToScreen would
            //    compute centering offsets for the old rect.
            _shell.ViewerColumn.UpdateLayout();

            // 5. Snap the image to the new fit immediately. The
            //    viewer is still at Opacity=0, so the snap is
            //    invisible. Skip the zoom animation because the
            //    200ms slide-in would be visible through the
            //    following 150ms opacity fade.
            _shell.FitImageToViewer();

            if (entering)
            {
                // Show both the exit hint AND the edge nav on
                // fullscreen entry. Previous behavior collapsed
                // the edge nav to invisible and only revealed it
                // on the first mouse move — which left a user
                // who tapped Ctrl+F and then sat still with no
                // prev/next affordance. Now both are visible
                // from t=0.
                ShowEdgeNav();
                // Show the exit hint. The hint auto-hides after
                // 5s (longer than the edge nav's 3s because the
                // "Esc / Ctrl+F" reminder is something the
                // user might need a moment to absorb, especially
                // on first fullscreen entry).
                ShowExitFullscreenHint();
                _exitHintHideTimer?.Stop();
                _exitHintHideTimer?.Start();
                _overlayHideTimer?.Stop();
            }
            else
            {
                // Re-focus the window so Esc / arrow keys
                // reach the keyboard handler after the HWND swap.
                _shell.Window.Focus();

                // If the user had the window Maximized before
                // fullscreen, re-maximize it on the next
                // dispatcher cycle (deferred so it doesn't
                // fight the WindowStyle HWND recreation).
                if (_previousWindowState == WindowState.Maximized)
                {
                    _shell.Window.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        _shell.Window.WindowState = WindowState.Maximized;
                    }), DispatcherPriority.Background);
                }
            }

            // 6. Fade the viewer back in over 150ms (ease-out).
            _shell.ViewerColumn.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(1d, TimeSpan.FromMilliseconds(150))
                {
                    EasingFunction = new CubicEase
                    {
                        EasingMode = EasingMode.EaseOut
                    }
                });
        };
        _shell.ViewerColumn.BeginAnimation(UIElement.OpacityProperty, fadeOut);

        // 7. Parallel: viewer background color cross-fade.
        //    Started alongside the fade-out so the background
        //    is mid-transition when the fade-in reveals the new
        //    view.
        AnimateViewerBackground(targetBackground, 150);
    }

    /// <summary>
    /// WindowState captured just before the entering
    /// transition, so the exit transition can restore it
    /// (e.g. re-maximize a previously-maximized window).
    /// Captured automatically inside <see cref="Toggle"/>
    /// on the entering direction only — the field is read
    /// by the exit transition's "restore Maximized" branch
    /// (see line ~230).
    /// </summary>
    private WindowState _previousWindowState;

    /// <summary>
    /// Cross-fade the viewer column background over 200ms.
    /// SolidColorBrush is mutated via ColorAnimation; the
    /// brush instance is kept (so the rest of the visual
    /// tree that referenced it stays valid) and only the
    /// underlying color animates.
    /// </summary>
    private void AnimateViewerBackground(Brush target, double durationMs = 200)
    {
        if (target is not SolidColorBrush targetSolid) return;
        // Clone the resource brush so we own the color
        // (resource brushes are shared/frozen; we can't
        // mutate them).
        var current = _shell.ViewerColumn.Background as SolidColorBrush;
        if (current == null || current.IsFrozen)
        {
            // Seed the new brush with the theme's resting
            // color instead of Colors.White. The previous
            // fallback worked only because SurfaceElevated
            // happened to be white; if the design token ever
            // changes, the first cross-fade would visibly flash
            // from white to the new color.
            var seed = current?.Color
                ?? ((SolidColorBrush)_theme.SurfaceElevated).Color;
            current = new SolidColorBrush(seed);
        }
        else
        {
            // Detach the previous animation so the new one wins.
            current.BeginAnimation(SolidColorBrush.ColorProperty, null);
        }
        _shell.ViewerColumn.Background = current;
        var anim = new ColorAnimation
        {
            From = current.Color,
            To = targetSolid.Color,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            EasingFunction = new CubicEase
            {
                EasingMode = EasingMode.EaseInOut
            }
        };
        current.BeginAnimation(SolidColorBrush.ColorProperty, anim);
    }

    // ---- Edge nav (fullscreen-only) ----

    public void OnMouseMove(Point position)
    {
        if (!_isFullscreen) return;
        if (Math.Abs(position.X - _lastMousePosition.X) > 5 ||
            Math.Abs(position.Y - _lastMousePosition.Y) > 5)
        {
            _lastMousePosition = position;
            // ShowEdgeNav no-ops if already visible AND
            // (since the P1 restoration) restarts the 3s
            // auto-hide timer via ResetOverlayHideTimer. So
            // a continuous mouse-move stream keeps the
            // edge nav visible; once the mouse stops for
            // 3s, the timer fires and the buttons fade out.
            ShowEdgeNav();
        }
    }

    /// <summary>
    /// Show the fullscreen edge-nav buttons (left + right).
    /// Cancels any in-flight fade-out, makes the UserControls
    /// Visible, and animates their Opacity 0→1 over 200ms.
    /// The 3s auto-hide timer is (re)started so the buttons
    /// fade away after the user stops moving the mouse.
    ///
    /// P1 fix: animates the UserControl's Opacity, not the
    /// inner Border's. The UserControl has Opacity=0 in
    /// XAML; the WPF compositor multiplies parent×child
    /// Opacity, so animating the child Border was a no-op
    /// (the parent's 0 won).
    /// </summary>
    private void ShowEdgeNav()
    {
        if (_edgeNavVisible) { ResetOverlayHideTimer(); return; }
        _edgeNavVisible = true;
        _shell.EdgeNavLeft.BeginAnimation(UIElement.OpacityProperty, null);
        _shell.EdgeNavRight.BeginAnimation(UIElement.OpacityProperty, null);
        _shell.EdgeNavLeft.Visibility = Visibility.Visible;
        _shell.EdgeNavRight.Visibility = Visibility.Visible;
        var fadeIn = new DoubleAnimation(0d, 1d, TimeSpan.FromMilliseconds(200));
        _shell.EdgeNavLeft.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        _shell.EdgeNavRight.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        ResetOverlayHideTimer();
    }

    /// <summary>
    /// Fade the edge-nav buttons out (200ms) and collapse
    /// them once the animation completes. Safe to call when
    /// already hidden.
    /// </summary>
    private void HideEdgeNav()
    {
        if (!_edgeNavVisible) return;
        _edgeNavVisible = false;
        var fadeOut = new DoubleAnimation(1d, 0d, TimeSpan.FromMilliseconds(200));
        fadeOut.Completed += (_, _) =>
        {
            if (_edgeNavVisible) return;
            _shell.EdgeNavLeft.Visibility = Visibility.Collapsed;
            _shell.EdgeNavRight.Visibility = Visibility.Collapsed;
            _shell.EdgeNavLeft.Opacity = 0;
            _shell.EdgeNavRight.Opacity = 0;
        };
        _shell.EdgeNavLeft.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        _shell.EdgeNavRight.BeginAnimation(UIElement.OpacityProperty, fadeOut);
    }

    private void ResetOverlayHideTimer()
    {
        _overlayHideTimer?.Stop();
        _overlayHideTimer?.Start();
    }

    private void ShowExitFullscreenHint()
    {
        // P1 fix: animate the UserControl's Opacity, not the
        // inner Border's. The XAML declares the
        // ExitFullscreenHintView with Opacity="0", and WPF
        // composites parent×child Opacity — so animating
        // the child Border was a no-op. The
        // TranslateTransform stays on the Border (the slide
        // animation reads the actual element's position) —
        // that's separate from the visibility problem.
        _shell.ExitHint.Visibility = Visibility.Visible;
        _shell.ExitHint.BeginAnimation(UIElement.OpacityProperty, null);
        _shell.ExitHintTransform.BeginAnimation(TranslateTransform.YProperty, null);
        var fadeIn = new DoubleAnimation(0d, 1d, TimeSpan.FromMilliseconds(200));
        var slideIn = new DoubleAnimation(-50d, 0d, TimeSpan.FromMilliseconds(200))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        _shell.ExitHint.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        _shell.ExitHintTransform.BeginAnimation(TranslateTransform.YProperty, slideIn);
    }

    private void HideExitFullscreenHint()
    {
        _shell.ExitHint.BeginAnimation(UIElement.OpacityProperty, null);
        _shell.ExitHintTransform.BeginAnimation(TranslateTransform.YProperty, null);
        var fadeOut = new DoubleAnimation(1d, 0d, TimeSpan.FromMilliseconds(200));
        var slideOut = new DoubleAnimation(0d, -50d, TimeSpan.FromMilliseconds(200))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        _shell.ExitHint.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        _shell.ExitHintTransform.BeginAnimation(TranslateTransform.YProperty, slideOut);
    }

    // ---- Floating tree popup (hot zone + panel) ----
    //
    // The popup lives on the root grid (TreeHotZone +
    // TreeFloatingPopup are siblings of the main 3-column
    // layout), so we expose them through the shell too.

    /// <summary>Handler for the 8px left-edge hot zone's
    /// MouseEnter. Opens the tree floating popup if the
    /// inline tree is currently collapsed (in fullscreen, the
    /// tree is always hidden). The popup's Child (the
    /// FolderTreeFloating UserControl) is wired by MainWindow
    /// once at startup; the controller only flips IsOpen +
    /// offset.</summary>
    public void TreeHotZone_MouseEnter()
    {
        if (_isFullscreen || !_isTreeVisible) return;
        _treeHideTimer?.Stop();
        if (!_shell.TreeFloatingPopup.IsOpen)
        {
            _shell.TreeFloatingPopup.HorizontalOffset = 0;
            _shell.TreeFloatingPopup.VerticalOffset = 0;
            // Sync the floating tree's items / selection to
            // mirror the inline tree (so re-opens after
            // folder navigation show the right state).
            _shell.SyncFloatingTree();
            _shell.TreeFloatingPopup.IsOpen = true;
        }
    }

    /// <summary>Don't close the popup yet — the user is
    /// probably moving their cursor INTO the floating tree.
    /// The popup's own MouseLeave handler fires when the
    /// cursor truly exits both the hot zone and the popup, at
    /// which point we close.</summary>
    public void TreeHotZone_MouseLeave() { }

    public void TreeFloatingPanel_MouseEnter()
    {
        _treeHideTimer?.Stop();
    }

    /// <summary>Mouse left the floating panel. Start a short
    /// hide timer so the panel doesn't close while the cursor
    /// is crossing the 4px gap between the popup edge and
    /// the next target. If the cursor re-enters within 250ms
    /// (via hot zone or panel), the timer is cancelled.</summary>
    public void TreeFloatingPanel_MouseLeave()
    {
        if (_treeHideTimer == null)
        {
            _treeHideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _treeHideTimer.Tick += (_, _) =>
            {
                _treeHideTimer.Stop();
                _shell.TreeFloatingPopup.IsOpen = false;
            };
        }
        _treeHideTimer.Stop();
        _treeHideTimer.Start();
    }

    public void TreeFloatingPopup_Closed()
    {
        _treeHideTimer?.Stop();
    }

    private bool _isTreeVisible = true;
    /// <summary>True when the user has the folder tree column
    /// visible. Mirrors MainWindow's local field so the
    /// TreeHotZone_MouseEnter guard can skip the popup in
    /// non-fullscreen when the tree is already showing.</summary>
    public bool IsTreeVisible
    {
        get => _isTreeVisible;
        set => _isTreeVisible = value;
    }

    // ---- Cleanup ----

    public void Detach()
    {
        _overlayHideTimer?.Stop();
        _exitHintHideTimer?.Stop();
        _treeHideTimer?.Stop();
        // Cancel any in-flight fade-in / fade-out on the
        // viewer column so its Completed callback doesn't
        // run after detach.
        _shell.ViewerColumn.BeginAnimation(UIElement.OpacityProperty, null);
        _shell.EdgeNavLeft.BeginAnimation(UIElement.OpacityProperty, null);
        _shell.EdgeNavRight.BeginAnimation(UIElement.OpacityProperty, null);
        _shell.ExitHint.BeginAnimation(UIElement.OpacityProperty, null);
    }
}
