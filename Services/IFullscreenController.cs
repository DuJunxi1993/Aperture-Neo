using System;
using System.Windows;

namespace ApertureNeo.Services;

/// <summary>
/// Window-level fullscreen state machine + animations,
/// extracted from MainWindow.Transitions. Owns the
/// transition-generation counter, the 3s overlay-hide timer
/// (edge nav) + 5s exit-hint timer, the edge-nav show/hide
/// animations, the exit-hint fade + slide animations, the
/// viewer-background cross-fade, the tree floating popup
/// (hot zone + 250ms close timer), and the
/// WPF-UI ClientAreaBorder padding reset.
///
/// The controller is constructed by MainWindow in its
/// constructor (after InitializeComponent has resolved all
/// the XAML element references) and stored as a field. It
/// is not registered in DI because no other component needs
/// to resolve it — the IPluginContext / VMs observe
/// IUiState.IsFullscreen instead. The interface exists for
/// testability and to give the state machine a clean home
/// outside the 800-line MainWindow.xaml.cs.
///
/// Visual elements + chrome-update callbacks are passed in
/// via <see cref="FullscreenShell"/> (a data carrier with
/// <c>required init</c> properties). MainWindow fills in the
/// shell at construction time.
///
/// Tree popup logic lives here too even though it isn't
/// strictly fullscreen-related — it was already in
/// MainWindow.Transitions.cs and grouping it with the
/// other "window-level visual transitions" logic keeps
/// MainWindow.Input.cs the only place that handles
/// keyboard / drag / drop input.
/// </summary>
public interface IFullscreenController
{
    bool IsFullscreen { get; }

    /// <summary>Toggle between fullscreen and windowed. Triggers
    /// the 300ms transition (150ms fade-out, OS swap, 150ms
    /// fade-in). No-op if the transition generation has moved
    /// on (rapid Ctrl+F, Ctrl+F) — only the latest toggle
    /// applies its OS swap. The pre-toggle WindowState is
    /// captured automatically on the entering direction so
    /// the exit transition can restore a previously-
    /// maximized window; callers do not need to notify
    /// separately.</summary>
    void Toggle();

    /// <summary>Called from MainWindow's MouseMove handler in
    /// fullscreen mode. Shows the edge-nav buttons (if not
    /// already visible) and resets the 3s auto-hide timer so
    /// the buttons stay visible while the mouse keeps moving.
    /// </summary>
    void OnMouseMove(Point position);

    /// <summary>Stop timers + cancel any in-flight transition
    /// callbacks. Called from MainWindow.Closed so the
    /// per-frame delegates are released before the window is
    /// GC'd.</summary>
    void Detach();

    // ---- Tree floating popup event handlers ----
    // Plain pass-throughs for the XAML MouseEnter / MouseLeave
    // / Closed events on the popup-related elements. The
    // controller owns the open/close state + the 250ms hide
    // timer; these methods just translate the WPF events into
    // internal state transitions.

    /// <summary>Handler for the 8px left-edge hot zone's
    /// MouseEnter. Opens the tree floating popup if the
    /// inline tree is currently collapsed (in fullscreen, the
    /// tree is always hidden).</summary>
    void TreeHotZone_MouseEnter();

    /// <summary>Handler for the hot zone's MouseLeave. Empty
    /// by design — the popup's own MouseLeave handler
    /// (below) is what actually starts the close timer,
    /// because the user is probably moving the cursor INTO
    /// the popup itself.</summary>
    void TreeHotZone_MouseLeave();

    /// <summary>Handler for the popup panel's MouseEnter.
    /// Cancels the close timer so the panel doesn't disappear
    /// while the cursor is crossing its surface.</summary>
    void TreeFloatingPanel_MouseEnter();

    /// <summary>Handler for the popup panel's MouseLeave.
    /// Starts the 250ms close timer.</summary>
    void TreeFloatingPanel_MouseLeave();

    /// <summary>Handler for the popup's Closed event. Defensive
    /// timer stop.</summary>
    void TreeFloatingPopup_Closed();
}
