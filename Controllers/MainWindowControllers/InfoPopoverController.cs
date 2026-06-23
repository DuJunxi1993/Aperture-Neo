using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ApertureNeo.Controls;
using ApertureNeo.Helpers;
using ApertureNeo.Models;

namespace ApertureNeo;

/// <summary>
/// Info-pill + popover concerns: the small pill in the
/// top-right of the viewer (resolution text + status dot),
/// the popover it opens (file size / dimensions / EXIF), and
/// the window-level click-outside-to-dismiss behaviour for
/// the popover.
///
/// P2 cleanup: removed the thumbnail-grid concerns (per-item
/// click, CollectionChanged handler, ThumbScroller_ScrollChanged,
/// ThumbGrid_Loaded). Those moved into:
///   - ThumbnailPanelVM (ThumbClicked command +
///     RequestScrollIntoView event)
///   - MainWindow.xaml.cs (inline lambdas for _navigation
///     subscriptions + OnThumbGridReady /
///     OnThumbScrollerScrollChanged for the visual-tree
///     wiring that needs _thumbCoordinator)
/// OnCurrentImageChanged also moved (split): the title +
/// context-menu-close lines live in MainWindow.xaml.cs now;
/// the thumb scroll-into-view is a VM event; the
/// ImageViewer.LoadImage call was redundant with
/// ImageViewerPanelViewModel.OnCurrentImageChanged.
/// </summary>
public partial class MainWindow
{

    /// <summary>
    /// R70: close the info popover when the user clicks anywhere
    /// outside both the InfoPill and the popover itself. We hook
    /// PreviewMouseLeftButtonDown at the window level (tunneling)
    /// so we see every click before the popup's own auto-close
    /// logic runs. Clicks on the pill itself are ignored here
    /// because the pill's MouseLeftButtonUp toggles
    /// the popover; clicks on the popover's body are ignored
    /// because we want the user to be able to select text inside
    /// the popover (e.g. the file name) without dismissing it.
    /// </summary>
    private void OnWindowPreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!InfoPopover.IsOpen) return;
        var src = e.OriginalSource as DependencyObject;
        // Click on the pill: the pill's MouseLeftButtonUp toggles.
        if (VisualTreeHelpers.IsDescendantOf(src, InfoPillContent)) return;
        // Click inside the popover's content: keep open so the
        // user can select / interact with the text inside.
        if (InfoPopover.Child is DependencyObject popoverChild
            && VisualTreeHelpers.IsDescendantOf(src, popoverChild)) return;
        // Click anywhere else: close.
        InfoPopover.IsOpen = false;
    }

    /// <summary>
    /// R70: right-align the open info popover so its right edge
    /// matches the pill's right edge. Called from a Dispatcher
    /// callback at Loaded priority so the popover's child has been
    /// measured and ActualWidth is available. We set
    /// VerticalOffset to 6 for the breathing-room gap below the
    /// pill (the default WPF placement aligns the popover's top
    /// with the pill's bottom, so 6px of visual gap reads better).
    /// </summary>
    private void AlignPopoverToPillRight()
    {
        if (!InfoPopover.IsOpen) return;
        var popoverChild = InfoPopover.Child as FrameworkElement;
        if (popoverChild == null) return;

        double pillWidth = InfoPillContent.ActualWidth;
        double popoverWidth = popoverChild.ActualWidth;
        if (popoverWidth <= 0) return;

        // Right-align: shift the popover left so its right edge
        // aligns with the pill's right edge. HorizontalOffset is
        // added to the default position (popover's left edge at
        // pill's left edge), so the shift is the difference between
        // the two widths.
        InfoPopover.HorizontalOffset = pillWidth - popoverWidth;
        InfoPopover.VerticalOffset = 6;

        // Screen-bottom safety: if the popover would extend past
        // the bottom of the work area, flip it above the pill.
        // Compute the popover's current screen bottom from the
        // pill's screen position + VerticalOffset + ActualHeight.
        var popoverTopOnScreen = InfoPillContent.PointToScreen(
            new Point(0, InfoPillContent.ActualHeight + InfoPopover.VerticalOffset)).Y;
        var popoverBottomOnScreen = popoverTopOnScreen + popoverChild.ActualHeight;
        var workArea = SystemParameters.WorkArea;
        if (popoverBottomOnScreen > workArea.Bottom)
        {
            // Flip above the pill: the popover's top should sit
            // 6px above the pill's top edge.
            InfoPopover.VerticalOffset = -(popoverChild.ActualHeight + 6);
        }
    }

    private void InfoPopover_Closed(object? sender, EventArgs e)
    {
        // Reset the cursor to the default so the InfoPill doesn't
        // look "pressed" after the popover closes. The cursor
        // property is set in XAML, so this is purely a visual nicety.
    }
}
