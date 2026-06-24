using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using ApertureNeo.ViewModels;

namespace ApertureNeo.Views;

/// <summary>
/// Floating navigation bar. P2: binds to
/// <see cref="FloatingBarViewModel"/> via DataContext (set in
/// ctor from DI). Buttons use Command="{Binding XxxCommand}".
/// The Fit / ZoomToOriginal commands are present on the VM for
/// XAML binding but currently no-op (the original Action callbacks
/// that would have routed the click into MainWindow's viewer
/// were never wired up; tracked as a known gap).
/// </summary>
public partial class FloatingBarView : UserControl
{
    public FloatingBarView()
    {
        InitializeComponent();
        DataContext = AppHost.Services?.GetService<FloatingBarViewModel>();
    }

    public Border FloatingBarContentRef => FloatingBarContent;
}