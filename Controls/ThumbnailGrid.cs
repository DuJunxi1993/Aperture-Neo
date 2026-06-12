using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ApertureNeo.Models;

namespace ApertureNeo.Controls;

/// <summary>
/// Thumbnail grid. Inherits from <see cref="ListBox"/> so that
/// WPF's built-in virtualizing pipeline (ItemContainerGenerator +
/// IScrollInfo on the items panel + Recycling container strategy)
/// actually works — ItemsControl alone only wires up the
/// generator, but doesn't tell the panel to realize containers on
/// demand. A plain ItemsControl + our AutoFitPanel + IsVirtualizing
/// True leaves the visible band empty (containers never generated),
/// which is why the earlier custom-VirtualizingPanel path showed
/// a blank grid. ListBox solves this by overriding the panel
/// hooks internally.
/// </summary>
public class ThumbnailGrid : ListBox
{
    public static readonly DependencyProperty SelectedItemProperty =
        DependencyProperty.Register(nameof(SelectedItem), typeof(ImageItem), typeof(ThumbnailGrid),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnSelectedItemChanged));

    public ThumbnailGrid()
    {
        // Selection state is tracked by ThumbnailItem.IsSelected
        // (driven from our SelectedItem DP and the data trigger in
        // the card template). We do NOT want the ListBox's own
        // SelectedItem / selection chrome interfering — disable
        // built-in selection so mouse clicks reach the cards
        // unaltered.
        SelectionMode = SelectionMode.Single;
        // UI virtualization is a no-op for the current Round 54
        // AutoFitPanel (which is now a plain Panel, not a
        // VirtualizingPanel) — every ThumbnailItem is realized up
        // front. We still set IsVirtualizing=true so that if a
        // future round swaps AutoFitPanel back to a
        // VirtualizingPanel subclass the framework can take over
        // container realization without us having to rewire the
        // host. CanContentScroll is set to false because Panel
        // doesn't speak the IScrollInfo-in-item-units protocol
        // the framework expects — pixel scrolling is what we
        // want for a thumbnail grid anyway.
        VirtualizingPanel.SetIsVirtualizing(this, true);
        VirtualizingPanel.SetVirtualizationMode(this, VirtualizationMode.Recycling);
        ScrollViewer.SetCanContentScroll(this, false);
    }

    private static void OnSelectedItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ThumbnailGrid grid)
            grid.UpdateSelection();
    }

    public new ImageItem? SelectedItem
    {
        get => (ImageItem?)GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    public event Action<ImageItem>? ItemClicked;

    protected override bool IsItemItsOwnContainerOverride(object item) => item is ThumbnailItem;

    protected override DependencyObject GetContainerForItemOverride() => new ThumbnailItem { Owner = this };

    protected override void PrepareContainerForItemOverride(DependencyObject element, object item)
    {
        base.PrepareContainerForItemOverride(element, item);
        if (element is ThumbnailItem ti && item is ImageItem ii)
        {
            ti.ImageItem = ii;
            ti.Owner = this;
            ti.IsSelected = ReferenceEquals(ii, SelectedItem);
        }
    }

    /// <summary>
    /// Suppress the ListBox default selection chrome (the blue
    /// focus ring, the keyboard "selected item" highlight). The
    /// thumbnail card renders its own selection visual from
    /// ThumbnailItem.IsSelected, and the focus ring interferes
    /// with the hover state in practice. Selection state still
    /// flows through our SelectedItem DP.
    /// </summary>
    protected override void OnSelectionChanged(SelectionChangedEventArgs e)
    {
        // Intentionally do not call base: ListBox default behavior
        // would mark the container as Selected via the Selector
        // pipeline and apply its keyboard focus visual. We mirror
        // the active container's selection state on the next
        // layout pass through UpdateSelection.
        if (SelectedItem != null && ItemContainerGenerator.ContainerFromItem(SelectedItem) is ThumbnailItem ti)
        {
            ti.IsSelected = true;
        }
    }

    public void NotifyItemClicked(ImageItem item)
    {
        SelectedItem = item;
        UpdateSelection();
        ItemClicked?.Invoke(item);
    }

    public void ScrollSelectedIntoView()
    {
        if (SelectedItem == null) return;
        var idx = Items.IndexOf(SelectedItem);
        if (idx < 0) return;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            var container = ItemContainerGenerator.ContainerFromIndex(idx) as FrameworkElement;
            container?.BringIntoView();
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    private void UpdateSelection()
    {
        for (int i = 0; i < Items.Count; i++)
        {
            if (ItemContainerGenerator.ContainerFromIndex(i) is ThumbnailItem ti)
            {
                ti.IsSelected = ReferenceEquals(ti.ImageItem, SelectedItem);
            }
        }
    }
}

public class ThumbnailItem : ContentControl
{
    public static readonly DependencyProperty IsSelectedProperty =
        DependencyProperty.Register(nameof(IsSelected), typeof(bool), typeof(ThumbnailItem),
            new PropertyMetadata(false, OnIsSelectedChanged));

    private static void OnIsSelectedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ThumbnailItem item) item.UpdateVisual();
    }

    public bool IsSelected
    {
        get => (bool)GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }

    public ThumbnailGrid? Owner { get; set; }

    public ImageItem? ImageItem
    {
        get => DataContext as ImageItem;
        set => DataContext = value;
    }

    private static Brush SelectedBackground => (Brush)Application.Current.Resources["ThumbnailSelectedBg"];
    private static Brush HoverBackground => (Brush)Application.Current.Resources["ThumbnailHoverBg"];
    private static readonly Brush Transparent = Brushes.Transparent;

    public ThumbnailItem()
    {
        Cursor = Cursors.Hand;
        Focusable = false;
        FocusVisualStyle = null;
        // No theme-change subscription needed: the app is Linear
        // light-mode only, so SelectedBackground / HoverBackground
        // resolve to the same brushes for the lifetime of the grid.
        UpdateVisual();
    }

    private void UpdateVisual()
    {
        if (IsSelected)
        {
            BorderThickness = new Thickness(0);
            BorderBrush = Transparent;
            Background = SelectedBackground;
        }
        else
        {
            BorderThickness = new Thickness(0);
            BorderBrush = Transparent;
            Background = Transparent;
        }
    }

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        if (!IsSelected) Background = HoverBackground;
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        Background = IsSelected ? SelectedBackground : Transparent;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (ImageItem != null && Owner != null)
        {
            Owner.NotifyItemClicked(ImageItem);
        }
        e.Handled = true;
    }
}
