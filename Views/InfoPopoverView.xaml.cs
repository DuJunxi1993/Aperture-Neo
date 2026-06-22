using System.Windows.Controls;

namespace ApertureNeo.Views;

/// <summary>
/// Body of the info popover (file name / size+dimensions / EXIF).
/// Migrated from MainWindow.xaml. The host (MainWindow) owns
/// the <see cref="System.Windows.Controls.Primitives.Popup"/>
/// that hosts this UserControl; the popover's lifetime
/// (open/close) and positioning (right-align with pill) are
/// still controlled by the host because they depend on
/// multiple external factors (fullscreen state, the
/// InfoPillView's position).
/// </summary>
public partial class InfoPopoverView : UserControl
{
    public InfoPopoverView()
    {
        InitializeComponent();
    }

    public TextBlock PopoverFileNameRef => PopoverFileName;
    public TextBlock PopoverSizeRef => PopoverSize;
    public TextBlock PopoverDimensionsRef => PopoverDimensions;
    public TextBlock PopoverExifMakeRef => PopoverExifMake;
    public TextBlock PopoverExifModelRef => PopoverExifModel;
    public TextBlock PopoverExifDateRef => PopoverExifDate;
}