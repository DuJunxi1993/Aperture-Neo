using System.Windows.Controls;
using ApertureNeo.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace ApertureNeo.Views;

/// <summary>
/// Body of the info popover (file name / size+dimensions / EXIF).
/// P2: binds to <see cref="InfoPopoverViewModel"/> via
/// DataContext. The host (MainWindow) still owns the
/// <see cref="System.Windows.Controls.Primitives.Popup"/>
/// that hosts this UserControl; the popover's lifetime
/// (open/close) and positioning (right-align with pill) are
/// still controlled by the host because they depend on
/// multiple external factors.
/// </summary>
public partial class InfoPopoverView : UserControl
{
    public InfoPopoverView()
    {
        InitializeComponent();
        DataContext = AppHost.Services?.GetService<InfoPopoverViewModel>();
    }
}