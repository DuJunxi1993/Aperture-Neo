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
/// Info-pill + thumbnail-grid concerns: the small pill in the
/// top-right of the viewer (resolution text + status dot), the
/// popover it opens (file size / dimensions / EXIF), the
/// thumbnail grid's per-item click and selection handlers, and
/// the lazy ScrollChanged wiring inside ThumbGrid. Extracted
/// from MainWindow.xaml.cs as a partial class.
/// </summary>
public partial class MainWindow
{
    private void OnThumbClicked(ImageItem item) { _navigation.NavigateTo(item.FilePath); }

    private void OnCollectionChanged()
    {
        ThumbGrid.ItemsSource = _navigation.Items;
        ThumbEmpty.Visibility = _navigation.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _thumbCoordinator.LoadForFolder(_navigation.Items, _navigation.CurrentIndex, null);
    }

    /// <summary>
    /// Triggered by the thumbnail ScrollViewer. Asks the panel for the
    /// real visible index range (computed from the panel's measure
    /// pass — actual cell size and column count) and forwards that
    /// to the load coordinator. Cheap O(1) work per scroll tick.
    /// </summary>
    private void ThumbScroller_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_navigation.Count == 0 || _autoFit == null) return;
        var (firstIdx, lastIdx) = _autoFit.GetVisibleIndexRange(
            e.VerticalOffset, e.ViewportHeight, _navigation.Count);
        if (firstIdx < 0) return;
        _thumbCoordinator.EnsureVisible(firstIdx, lastIdx);
    }

    /// <summary>
    /// The ListBox (ThumbGrid) drives scrolling through its own
    /// internal ScrollViewer, which does pixel scrolling (Round
    /// 54 set <c>CanContentScroll=false</c> on ThumbGrid so the
    /// ScrollViewer scrolls by pixels, not items, which is what
    /// we want for a thumbnail grid). The inner ScrollViewer is
    /// created lazily from the ListBox template and isn't
    /// addressable directly from XAML. Hook Loaded on the
    /// ListBox (fires after the template has been applied and
    /// the visual tree is fully built), walk down to find the
    /// first ScrollViewer descendant, and subscribe
    /// ScrollChanged.
    /// </summary>
    private void ThumbGrid_Loaded(object sender, RoutedEventArgs e)
    {
        var innerScroller = VisualTreeHelpers.FindVisualChild<ScrollViewer>(ThumbGrid);
        if (innerScroller == null) return;
        innerScroller.ScrollChanged += ThumbScroller_ScrollChanged;
        // Round 68: wire the ThumbnailCache size provider to the
        // AutoFitPanel that lives inside ThumbGrid.ItemsPanel.
        // The cache was constructed in App.OnStartup with a default
        // 256px size; now that the panel exists, we can hand the
        // cache a live source that returns the actual per-cell
        // width each time a thumbnail is generated. A second
        // resolution pass on column resize re-evaluates the size
        // because the Func is captured by reference, not value.
        // Also cache the panel reference so ThumbScroller_ScrollChanged
        // can compute the actual visible item range (replaces the
        // hardcoded 152px / 2-col guess that mis-targeted the load
        // range on wide or narrow viewports).
        _autoFit = VisualTreeHelpers.FindVisualChild<AutoFitPanel>(ThumbGrid);
        if (_autoFit != null)
        {
            App.ThumbnailCache.SetSizeProvider(() => (int)_autoFit.ActualItemWidth);
        }
        // Re-raise the Loaded signal so handlers that depend on
        // the inner ScrollViewer's existence can re-run. Currently
        // no such dependency, but the call is cheap and future-
        // proofs against silent load-order issues.
        ThumbGrid_Ready?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Fired once ThumbGrid's template has been applied and the
    /// inner ScrollViewer is reachable. Currently unused; kept
    /// as a hook for any future initialisation that needs the
    /// inner ScrollViewer (e.g. custom keyboard navigation).
    /// </summary>
    public event EventHandler? ThumbGrid_Ready;

    private void OnCurrentImageChanged(ImageItem item)
    {
        if (item == null) return;
        Title = $"Aperture Neo · {item.FileName} ({_navigation.CurrentIndex + 1}/{_navigation.Count})";
        ImageViewer.LoadImage(item.FilePath);
        // P2: ImageIndexInfo.Text update moved to
        // FloatingBarViewModel (it binds to ImageIndexInfo
        // via XAML and updates on NavigationService events).
        ThumbGrid.SelectedItem = item;
        ThumbGrid.ScrollSelectedIntoView();
        if (ImageViewer.ContextMenu != null) ImageViewer.ContextMenu.IsOpen = false;
    }

    // P2: InfoPillViewModel owns ImageInfo + IsDimensionsKnown via
    // XAML bindings; the controller no longer needs to write them
    // directly.

    /// <summary>
    /// R70: toggle the info popover when the user clicks the
    /// InfoPill in the top-right of the viewer. Clicking the pill
    /// again closes the popover. Clicking anywhere outside both the
    /// pill and the popover also closes it (handled by the
    /// <see cref="OnWindowPreviewMouseLeftButtonDown"/> global
    /// handler). The popover is opened with
    /// <c>StaysOpen=true</c> because we manage its lifetime
    /// ourselves — the default <c>StaysOpen=false</c> would have
    /// the popover close itself on the very MouseDown that opened
    /// it (the pill click), which is the bug we hit in the last
    /// round.
    /// </summary>
    private void InfoPillContent_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (InfoPopover.IsOpen)
        {
            InfoPopover.IsOpen = false;
            return;
        }
        var item = _navigation.Current;
        if (item == null) return;
        PopulateInfoPopover(item);
        // We manage close-vs-stay-open ourselves (via the global
        // click-outside handler) so the popover's own auto-close
        // behavior would only interfere. StaysOpen=true disables
        // that auto-close.
        InfoPopover.StaysOpen = true;
        // Reset HorizontalOffset before opening so the popover
        // starts at the default left-aligned position; we'll
        // right-align it in a Dispatcher callback once the popover
        // has been measured and we know its actual width.
        InfoPopover.HorizontalOffset = 0;
        InfoPopover.IsOpen = true;
        // R70: right-align the popover's right edge with the pill's
        // right edge. We do this in a Dispatcher callback at
        // DispatcherPriority.Loaded so the popover's child has been
        // measured (ActualWidth is > 0) before we read it. The
        // callback also re-evaluates the screen-bottom clip and
        // flips the popover above the pill if needed.
        Dispatcher.BeginInvoke(new Action(AlignPopoverToPillRight),
            DispatcherPriority.Loaded);
    }

    /// <summary>
    /// R70: close the info popover when the user clicks anywhere
    /// outside both the InfoPill and the popover itself. We hook
    /// PreviewMouseLeftButtonDown at the window level (tunneling)
    /// so we see every click before the popup's own auto-close
    /// logic runs. Clicks on the pill itself are ignored here
    /// because the pill's own MouseLeftButtonUp handler toggles
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

    /// <summary>
    /// P2: PopulateInfoPopover's body is now
    /// <see cref="ApertureNeo.ViewModels.InfoPopoverViewModel.Refresh"/>.
    /// The popover's text blocks bind to the VM's properties via
    /// XAML; MainWindow's WireInfoPillEvents invokes
    /// vm.RefreshCommand on every popover open. This method
    /// is dead but kept as a marker for the controller sweep
    /// at the end of P2.
    /// </summary>
    private void PopulateInfoPopover(ImageItem item)
    {
        // No-op: replaced by InfoPopoverViewModel.Refresh.
    }

    private void InfoPopover_Closed(object? sender, EventArgs e)
    {
        // Reset the cursor to the default so the InfoPill doesn't
        // look "pressed" after the popover closes. The cursor
        // property is set in XAML, so this is purely a visual nicety.
    }
}
