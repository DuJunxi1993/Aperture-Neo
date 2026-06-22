using System;
using System.Windows;
using System.Windows.Controls;
using ApertureNeo.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace ApertureNeo.Views;

public enum EdgeDirection
{
    Left,
    Right
}

/// <summary>
/// One edge-nav button (left or right). Use the
/// <see cref="Direction"/> dependency property to set
/// which side. P2: binds to <see cref="EdgeNavViewModel"/>
/// via DataContext. Click bound to NavigateCommand; the VM
/// raises NavigateRequested event for the host to route to
/// MovePrev (left) or MoveNext (right).
///
/// P2: the host's EdgeNavController animation logic still
/// references <see cref="EdgeNavBorderRef"/> for show/hide.
/// </summary>
public partial class EdgeNavView : UserControl
{
    public static readonly DependencyProperty DirectionProperty =
        DependencyProperty.Register(
            nameof(Direction), typeof(EdgeDirection), typeof(EdgeNavView),
            new PropertyMetadata(EdgeDirection.Left, OnDirectionChanged));

    public EdgeDirection Direction
    {
        get => (EdgeDirection)GetValue(DirectionProperty);
        set => SetValue(DirectionProperty, value);
    }

    public EdgeNavView()
    {
        InitializeComponent();
        DataContext = AppHost.Services?.GetService<EdgeNavViewModel>();
        UpdateLayoutForDirection();
    }

    public Border EdgeNavBorderRef => EdgeNavBorder;

    private static void OnDirectionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((EdgeNavView)d).UpdateLayoutForDirection();
    }

    private void UpdateLayoutForDirection()
    {
        if (EdgeIcon == null) return;
        EdgeIcon.Symbol = Direction == EdgeDirection.Left
            ? Wpf.Ui.Controls.SymbolRegular.ChevronLeft20
            : Wpf.Ui.Controls.SymbolRegular.ChevronRight20;
    }
}