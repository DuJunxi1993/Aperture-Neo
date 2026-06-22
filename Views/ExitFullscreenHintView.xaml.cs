using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ApertureNeo.Views;

/// <summary>
/// "退出全屏 (Esc / Ctrl+F)" pill at the top of the viewer in
/// fullscreen. The host (MainWindow) still owns the show/hide
/// animation timer + the actual visibility state (this
/// UserControl is just a passive control surface). The host
/// accesses <see cref="HintBorderRef"/> and
/// <see cref="TransformRef"/> to drive the
/// fade + slide animations.
/// </summary>
public partial class ExitFullscreenHintView : UserControl
{
    public ExitFullscreenHintView()
    {
        InitializeComponent();
    }

    public System.Windows.Controls.Border HintBorderRef => ExitFullscreenHint;
    public System.Windows.Media.TranslateTransform TransformRef => ExitFullscreenTransform;
}