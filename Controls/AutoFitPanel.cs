using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ApertureNeo.Controls;

/// <summary>
/// Multi-column wrap panel for the thumbnail grid. Computes a
/// column count from the available width and a
/// <see cref="MinItemSize"/> target, then arranges items row-by-row
/// with a fixed <see cref="Spacing"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a non-virtualizing Panel (Round 54):</b>
/// after Rounds 47-53 we exhausted every reasonable way to make
/// this work as a <see cref="VirtualizingPanel"/> subclass:
/// <list type="bullet">
/// <item>R51 tried <c>GenerateNext</c> on the generator — reset
///       GeneratorStatus, broke ContextMenu (it has its own
///       ItemContainerGenerator we were side-effecting).</item>
/// <item>R52 walked <c>InternalChildren</c> and called Measure on
///       each one — but on the first measure pass
///       <c>InternalChildren.Count = 0</c>, so the foreach did
///       nothing and the framework never realized any containers.</item>
/// </list>
/// WPF's <see cref="VirtualizingPanel"/> is abstract on
/// <c>MeasureOverride</c> and has no public
/// <c>EnsureRealizedItems()</c> method on .NET 10 (and never
/// has). The only way to force realization in a custom
/// VirtualizingPanel subclass is to walk the generator directly
/// via the framework's internal API — fragile, version-dependent,
/// and out of scope for the current refactor.
/// </para>
/// <para>
/// Reverting to plain <see cref="Panel"/> means every item is
/// measured up front — fine for the 5-item test folder and the
/// 200-image photo album use case. A future round can revisit
/// virtualizing-wrap by either (a) implementing a full
/// <c>IItemContainerGenerator</c> replacement, or (b) viewport
/// culling on a non-virtualizing Panel (measure only items in
/// the visible band and stub-Visualize the rest).
/// </para>
/// <para>
/// <b>Scrolling:</b> pairing with
/// <see cref="ScrollViewer.CanContentScroll"/>=<c>false</c> on
/// the host ListBox gives pixel scrolling, which is what we
/// want for a thumbnail grid anyway (item-aligned scrolling
/// jumps the viewport by 100+ pixels at a time on small panels).
/// </para>
/// </remarks>
public class AutoFitPanel : Panel
{
    public static readonly DependencyProperty MinItemSizeProperty =
        DependencyProperty.Register(nameof(MinItemSize), typeof(double), typeof(AutoFitPanel),
            new FrameworkPropertyMetadata(150.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty SpacingProperty =
        DependencyProperty.Register(nameof(Spacing), typeof(double), typeof(AutoFitPanel),
            new FrameworkPropertyMetadata(6.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public double MinItemSize
    {
        get => (double)GetValue(MinItemSizeProperty);
        set => SetValue(MinItemSizeProperty, value);
    }

    public double Spacing
    {
        get => (double)GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    /// <summary>
    /// Round 56: dropped the file-name label from
    /// <c>LinearThumbnailTemplate</c>, so the panel no longer
    /// needs to reserve vertical space below the card for text.
    /// Each cell is now a pure square (itemWidth × itemWidth).
    /// </summary>

    // Round 69: viewport culling. The owning ScrollViewer's
    // offset and viewport height determine which children are
    // visible. Only visible children (plus a buffer zone) get
    // full measure/arrange; the rest get zero size so their
    // visual trees are effectively invisible and don't
    // participate in layout. Found on visual-tree connect via
    // OnVisualParentChanged; null before first connect (measure
    // falls back to "all children visible").
    private ScrollViewer? _scrollViewer;
    // Cached visible range from the last scroll-invalidation.
    // Prevents redundant InvalidateMeasure when the band hasn't
    // shifted. (-1, -1) = unknown / uninitialised.
    private int _lastVisibleFirst = -1;
    private int _lastVisibleLast = -1;

    // Round 69: extra rows above and below the visible band
    // that still get full measure/arrange. Prevents blank cells
    // during fast fling scrolling — the scroll event may be
    // throttled by WPF's layout queue, so the buffer absorbs
    // the velocity until the next layout pass catches up.
    private const int VisibleBufferRows = 4;

    // Cached last-measure layout values. Panel re-enters
    // MeasureOverride on width changes (column count changes)
    // and on items-changed events, so we recompute these every
    // measure pass.
    private double _itemWidth;
    private double _itemHeight;
    private int _cols;

    /// <summary>
    /// Round 68: the actual rendered width of a single cell after the
    /// most recent measure pass. The thumbnail loader reads this to
    /// size generated thumbnails to exactly what the cell will display
    /// (rather than a fixed 256×256 that's 4-5× the on-screen pixel
    /// area). Reading <see cref="ActualWidth"/> on the panel itself
    /// would give the total panel width, not the per-cell width.
    /// Returns 0 if the panel hasn't been measured yet.
    /// </summary>
    public double ActualItemWidth => _itemWidth;

    /// <summary>
    /// Convert a vertical scroll offset + viewport height into the
    /// index range of items currently visible in the panel. Replaces
    /// the host-side hardcoded row-height / column-count guesses
    /// (previously 152px / 2 cols in <c>ThumbScroller_ScrollChanged</c>)
    /// that mis-targeted the load range on wide or narrow viewports.
    /// Returns (-1, -1) before the panel has been measured (cell
    /// width still 0) or when <paramref name="itemCount"/> is 0.
    /// </summary>
    public (int firstIdx, int lastIdx) GetVisibleIndexRange(double verticalOffset, double viewportHeight, int itemCount)
    {
        if (_itemWidth <= 0 || _cols <= 0 || itemCount <= 0)
            return (-1, -1);

        double rowStride = _itemWidth + Spacing;
        int firstRow = Math.Max(0, (int)(verticalOffset / rowStride));
        int visibleRows = Math.Max(1, (int)(viewportHeight / rowStride) + 1);
        int firstIdx = firstRow * _cols;
        int lastIdx = Math.Min(itemCount - 1, (firstRow + visibleRows) * _cols - 1);
        return (firstIdx, lastIdx);
    }

    protected override void OnVisualParentChanged(DependencyObject oldParent)
    {
        base.OnVisualParentChanged(oldParent);
        FindScrollViewer();
    }

    private void FindScrollViewer()
    {
        if (_scrollViewer != null)
            _scrollViewer.ScrollChanged -= OnScrollChanged;

        _scrollViewer = FindAncestor<ScrollViewer>(this);

        if (_scrollViewer != null)
        {
            _scrollViewer.ScrollChanged += OnScrollChanged;
            _lastVisibleFirst = -1;
            _lastVisibleLast = -1;
        }
    }

    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_cols <= 0 || _itemWidth <= 0) return;
        int count = InternalChildren.Count;
        if (count == 0) return;

        var (first, last) = GetVisibleIndexRange(
            e.VerticalOffset, e.ViewportHeight, count);
        if (first < 0) return;

        int extra = VisibleBufferRows * _cols;
        first = Math.Max(0, first - extra);
        last = Math.Min(count - 1, last + extra);

        if (first != _lastVisibleFirst || last != _lastVisibleLast)
        {
            _lastVisibleFirst = first;
            _lastVisibleLast = last;
            InvalidateMeasure();
        }
    }

    private static T? FindAncestor<T>(DependencyObject obj) where T : DependencyObject
    {
        while (obj != null)
        {
            if (obj is T match) return match;
            obj = VisualTreeHelper.GetParent(obj);
        }
        return null;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 1000 : availableSize.Width;
        if (width < 1) width = 1;

        _cols = Math.Max(1, (int)(width / (MinItemSize + Spacing)));
        _itemWidth = (width - (_cols - 1) * Spacing) / _cols;
        if (_itemWidth < 1) _itemWidth = 1;

        // Square card area only. Round 56 dropped the file-name
        // label from LinearThumbnailTemplate, so each cell is a
        // pure square (itemWidth × itemWidth). The Border's
        // SquareAspectBehavior keeps the inner Card visually
        // square even if the panel's measured size drifts.
        _itemHeight = _itemWidth;

        int itemCount = InternalChildren.Count;

        // Round 69: viewport culling. Determine which children
        // are in or near the visible band. Non-visible children
        // get (0,0) measure so their visual trees are
        // effectively invisible and don't participate in layout.
        // Fall back to "all visible" when the ScrollViewer
        // hasn't been found yet (initial connect).
        int firstVisible = 0;
        int lastVisible = itemCount - 1;
        if (_scrollViewer != null && itemCount > 0)
        {
            var (first, last) = GetVisibleIndexRange(
                _scrollViewer.VerticalOffset,
                _scrollViewer.ViewportHeight,
                itemCount);
            if (first >= 0)
            {
                int extra = VisibleBufferRows * _cols;
                firstVisible = Math.Max(0, first - extra);
                lastVisible = Math.Min(itemCount - 1, last + extra);
            }
        }

        var fullSize = new Size(_itemWidth, _itemHeight);
        var zeroSize = new Size(0, 0);
        for (int i = 0; i < itemCount; i++)
        {
            var child = InternalChildren[i];
            child.Measure(i >= firstVisible && i <= lastVisible ? fullSize : zeroSize);
        }

        int rowCount = itemCount == 0 ? 0 : (itemCount + _cols - 1) / _cols;
        double totalHeight = rowCount * _itemHeight + Math.Max(0, rowCount - 1) * Spacing;
        return new Size(width, totalHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        int count = InternalChildren.Count;

        for (int i = 0; i < count; i++)
        {
            int col = i % _cols;
            int row = i / _cols;
            InternalChildren[i].Arrange(new Rect(
                col * (_itemWidth + Spacing),
                row * (_itemHeight + Spacing),
                _itemWidth, _itemHeight));
        }
        return finalSize;
    }
}
