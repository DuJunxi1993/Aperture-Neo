using System;
using System.Windows;
using System.Windows.Controls;

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

        // Plain Panel — measure every realized child with the
        // same square-card-plus-label extent. The owning
        // ListBox realizes all containers up front (no
        // virtualization), so InternalChildren.Count matches
        // Items.Count after the first measure.
        var childSize = new Size(_itemWidth, _itemHeight);
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(childSize);
        }

        int itemCount = InternalChildren.Count;
        int rowCount = itemCount == 0 ? 0 : (itemCount + _cols - 1) / _cols;
        double totalHeight = rowCount * _itemHeight + Math.Max(0, rowCount - 1) * Spacing;
        return new Size(width, totalHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        // Plain Panel — children are in 1:1 order with the
        // items, so index N in InternalChildren corresponds to
        // item index N. Compute (col, row) from the index and
        // place each child at that coordinate.
        int count = InternalChildren.Count;
        for (int i = 0; i < count; i++)
        {
            int col = i % _cols;
            int row = i / _cols;
            double x = col * (_itemWidth + Spacing);
            double y = row * (_itemHeight + Spacing);
            InternalChildren[i].Arrange(new Rect(x, y, _itemWidth, _itemHeight));
        }
        return finalSize;
    }
}
