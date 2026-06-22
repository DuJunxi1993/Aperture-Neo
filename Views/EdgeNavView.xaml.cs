using System;
using System.Windows;
using System.Windows.Controls;

namespace ApertureNeo.Views;

public enum EdgeDirection
{
    Left,
    Right
}

/// <summary>
/// One edge-nav button (left or right). Use the
/// <see cref="Direction"/> dependency property to set
/// which side. Click raises <see cref="Clicked"/>.
///
/// Migrated from MainWindow.xaml. The host (MainWindow) still
/// owns the show/hide animation timer and the visibility
/// (Visibility=Collapsed in window mode, animated Visible in
/// fullscreen) because those depend on shared fullscreen
/// state.
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
        UpdateLayoutForDirection();
    }

    public Border EdgeNavBorderRef => EdgeNavBorder;
    public Button BtnEdgeNavRef => BtnEdgeNav;

    /// <summary>Raised when the user clicks the edge button.</summary>
    public event EventHandler? Clicked;

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

    private void BtnEdgeNav_Click(object sender, RoutedEventArgs e) => Clicked?.Invoke(this, EventArgs.Empty);
}