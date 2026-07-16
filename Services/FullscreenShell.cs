using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ApertureNeo.Services;

/// <summary>
/// Visual elements + chrome-update callbacks the
/// <see cref="FullscreenController"/> needs. Passed in via
/// the constructor because the elements live in MainWindow's
/// XAML (not in DI) and the callbacks (FitImageToViewer,
/// ResetClientAreaBorderPadding, ApplyChrome) need access
/// to MainWindow's state machine.
///
/// All properties are <c>required init</c> — construction
/// fails at compile time if MainWindow forgets one, which
/// is much harder to miss than a silently-defaulted
/// reference.
/// </summary>
public sealed class FullscreenShell
{
    /// <summary>The host Window. Needed for
    /// WindowStyle / WindowState / Focus calls that the
    /// fullscreen transition performs. The controller
    /// doesn't hold a hard reference to MainWindow
    /// (only this Window) so it's testable with a
    /// stand-alone Window in a unit test.</summary>
    public required Window Window { get; init; }

    /// <summary>Left edge-nav UserControl. The controller
    /// animates its Opacity 0↔1 and toggles Visibility.</summary>
    public required UserControl EdgeNavLeft { get; init; }

    /// <summary>Right edge-nav UserControl.</summary>
    public required UserControl EdgeNavRight { get; init; }

    /// <summary>Exit-fullscreen hint UserControl at the top of
    /// the viewer. Opacity 0↔1 + Y translate.</summary>
    public required UserControl ExitHint { get; init; }

    /// <summary>TranslateTransform on the hint's Border. Drives
    /// the slide-in animation (Y -50 → 0).</summary>
    public required TranslateTransform ExitHintTransform { get; init; }

    /// <summary>The viewer column Grid. The controller
    /// animates its Opacity for the 150ms fade-out / fade-in
    /// and cross-fades its Background brush.</summary>
    public required Grid ViewerColumn { get; init; }

    /// <summary>Snap the current image to fit-to-viewer without
    /// the zoom animation. Called inside the transition's
    /// Completed callback so the image is ready before the
    /// 150ms fade-in reveals the new layout. Sets
    /// FitToScreenSkipAnimation = true then calls
    /// FitToScreen.</summary>
    public required Action FitImageToViewer { get; init; }

    /// <summary>Walk the visual tree and zero WPF-UI's
    /// ClientAreaBorder Padding (5px in Maximized state) so
    /// the viewer is edge-to-edge. Called both synchronously
    /// in Toggle and queued at Loaded priority in the
    /// transition completion.</summary>
    public required Action ResetClientAreaBorderPadding { get; init; }

    /// <summary>Apply the chrome (column visibility,
    /// title-bar / floating-bar / info-pill visibility)
    /// based on the current fullscreen state. Called by the
    /// controller at the start of the transition (so the
    /// chrome updates alongside the window state swap) and
    /// also by the non-fullscreen branch in UpdateOverlay
    /// Visibility — the controller dispatches the
    /// call, MainWindow owns the actual logic.</summary>
    public required Action ApplyChrome { get; init; }

    /// <summary>Tree floating popup. The controller drives
    /// the open/close + 250ms hide timer; the host wires
    /// the popup's Child once (the FolderTreeFloating
    /// UserControl in MainWindow.xaml). The Popup element
    /// itself is at the root grid level, sibling of the
    /// main 3-column layout, so it can't be reached via
    /// the column-4 Grid that contains the overlays.</summary>
    public required System.Windows.Controls.Primitives.Popup TreeFloatingPopup { get; init; }

    /// <summary>Sync the floating tree's selection +
    /// expansion state to match the inline tree. Called by
    /// the controller just before opening the popup so the
    /// floating tree visually mirrors the inline tree at
    /// first show (and on subsequent re-opens after the
    /// inline tree changed). MainWindow wires this to
    /// FolderTreeFloating.SyncFrom(FolderTree).</summary>
    public required Action SyncFloatingTree { get; init; }
}
