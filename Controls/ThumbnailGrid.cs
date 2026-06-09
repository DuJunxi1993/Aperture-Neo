using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ApertureNeo.Models;

namespace ApertureNeo.Controls;

public class ThumbnailGrid : ItemsControl
{
    public static readonly DependencyProperty SelectedItemProperty =
        DependencyProperty.Register(nameof(SelectedItem), typeof(ImageItem), typeof(ThumbnailGrid),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnSelectedItemChanged));

    private static void OnSelectedItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ThumbnailGrid grid)
            grid.UpdateSelection();
    }

    public ImageItem? SelectedItem
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
