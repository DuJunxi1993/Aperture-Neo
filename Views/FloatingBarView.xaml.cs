using System;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using ApertureNeo.ViewModels;

namespace ApertureNeo.Views;

/// <summary>
/// Floating navigation bar. P2: binds to
/// <see cref="FloatingBarViewModel"/> via DataContext (set in
/// ctor from DI). Buttons use Command="{Binding XxxCommand}".
/// Viewer-specific actions (Fit / ZoomToOriginal) subscribe to
/// VM events because the VM is UI-agnostic; the View knows
/// the actual <see cref="SkiaImageViewer"/> instance.
///
/// The host MainWindow sets <see cref="FitRequestedAction"/>
/// and <see cref="ZoomToOriginalRequestedAction"/> after
/// the view is constructed, so the view can call into the
/// viewer's methods without taking a hard dependency on
/// the viewer (which lives in MainWindow's XAML, not in
/// FloatingBarView's).
/// </summary>
public partial class FloatingBarView : UserControl
{
    public FloatingBarView()
    {
        InitializeComponent();
        DataContext = AppHost.Services?.GetService<FloatingBarViewModel>();
        if (DataContext is FloatingBarViewModel vm)
        {
            vm.FitRequested += (_, _) => FitRequestedAction?.Invoke();
            vm.ZoomToOriginalRequested += (_, _) => ZoomToOriginalRequestedAction?.Invoke();
        }
    }

    /// <summary>Action the host MainWindow assigns so the view
    /// can call FitToScreen on the actual SkiaImageViewer.</summary>
    public Action? FitRequestedAction { get; set; }

    public Action? ZoomToOriginalRequestedAction { get; set; }

    public Border FloatingBarContentRef => FloatingBarContent;
}