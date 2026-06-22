using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ApertureNeo.Views;

/// <summary>
/// Small pill in the top-right of the viewer showing the
/// current image's resolution + a status dot. Clicking it
/// raises <see cref="TogglePopoverRequested"/>; the host opens
/// the popover in response.
///
/// Migrated from MainWindow.xaml. Owns its own visual state
/// (the status dot's color is set via <see cref="SetStatusKnown"/>
/// / <see cref="SetStatusUnknown"/>). The host reads/writes
/// the displayed text via <see cref="ImageInfoRef"/>.
/// </summary>
public partial class InfoPillView : UserControl
{
    public InfoPillView()
    {
        InitializeComponent();
    }

    public Border InfoPillContentRef => InfoPillContent;
    public Border InfoPillDotRef => InfoPillDot;
    public TextBlock ImageInfoRef => ImageInfo;

    /// <summary>Set the status dot to "known" (indigo / BrandPrimary).</summary>
    public void SetStatusKnown()
    {
        InfoPillDot.Background = (Brush)Application.Current.Resources["BrandPrimary"];
    }

    /// <summary>Set the status dot to "unknown" (gray / TextQuat).</summary>
    public void SetStatusUnknown()
    {
        InfoPillDot.Background = (Brush)Application.Current.Resources["TextQuat"];
    }

    /// <summary>Raised when the user clicks the pill. Host decides
    /// whether to open or close the popover.</summary>
    public event EventHandler? TogglePopoverRequested;

    private void InfoPillContent_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        TogglePopoverRequested?.Invoke(this, EventArgs.Empty);
    }
}