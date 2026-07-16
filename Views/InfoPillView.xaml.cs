using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ApertureNeo.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace ApertureNeo.Views;

/// <summary>
/// Small pill in the top-right of the viewer showing the
/// current image's resolution + a status dot. P2: binds to
/// <see cref="InfoPillViewModel"/> via DataContext; the
/// ImageInfo text + status dot color are driven by the VM.
/// Click raises ToggleCommand on the VM; the host subscribes
/// to the VM's PopoverToggleRequested event to open / close
/// the popover.
/// </summary>
public partial class InfoPillView : UserControl
{
    public InfoPillView()
    {
        InitializeComponent();
        DataContext = AppHost.Services?.GetService<InfoPillViewModel>();
    }

    public Border InfoPillContentRef => InfoPillContent;
    public Border InfoPillDotRef => InfoPillDot;
}