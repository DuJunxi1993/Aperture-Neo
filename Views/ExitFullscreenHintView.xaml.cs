using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ApertureNeo.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace ApertureNeo.Views;

/// <summary>
/// "退出全屏 (Esc / Ctrl+F)" pill at the top of the viewer in
/// fullscreen. P2: binds to ExitFullscreenHintViewModel via
/// DataContext. The host (MainWindow) still owns the show/hide
/// animation timer (the VM only drives the Visibility flag
/// bound from IUiState.IsFullscreen). The host accesses
/// <see cref="HintBorderRef"/> and <see cref="TransformRef"/>
/// to drive the fade + slide animations.
/// </summary>
public partial class ExitFullscreenHintView : UserControl
{
    public ExitFullscreenHintView()
    {
        InitializeComponent();
        DataContext = AppHost.Services?.GetService<ExitFullscreenHintViewModel>();
    }

    public System.Windows.Controls.Border HintBorderRef => ExitFullscreenHint;
    public System.Windows.Media.TranslateTransform TransformRef => ExitFullscreenTransform;
}