using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ApertureNeo.Views;

/// <summary>
/// Floating navigation bar (prev/next/fit/slideshow/fullscreen +
/// image index + zoom %). Migrated from MainWindow.xaml.
///
/// The host (MainWindow) reads/writes <see cref="ImageIndexInfoRef"/>
/// and <see cref="SlideshowIconRef"/>. Click events are raised
/// as plain .NET events for the host to handle.
/// </summary>
public partial class FloatingBarView : UserControl
{
    public FloatingBarView()
    {
        InitializeComponent();
    }

    public System.Windows.Controls.Border FloatingBarContentRef => FloatingBarContent;
    public System.Windows.Controls.Button BtnPrevRef => BtnPrev;
    public System.Windows.Controls.Button BtnNextRef => BtnNext;
    public System.Windows.Controls.Button BtnFitRef => BtnFit;
    public System.Windows.Controls.Button BtnSlideshowRef => BtnSlideshow;
    public System.Windows.Controls.Button BtnFullscreenRef => BtnFullscreen;
    public System.Windows.Controls.TextBlock ImageIndexInfoRef => ImageIndexInfo;
    public System.Windows.Controls.TextBlock ZoomTextBlockRef => ZoomTextBlock;
    public Wpf.Ui.Controls.SymbolIcon SlideshowIconRef => SlideshowIcon;

    public event EventHandler? PrevClicked;
    public event EventHandler? NextClicked;
    public event EventHandler? FitClicked;
    public event EventHandler? SlideshowClicked;
    public event EventHandler? FullscreenClicked;
    public event EventHandler? ZoomTextClicked;

    private void BtnPrev_Click(object sender, RoutedEventArgs e) => PrevClicked?.Invoke(this, EventArgs.Empty);
    private void BtnNext_Click(object sender, RoutedEventArgs e) => NextClicked?.Invoke(this, EventArgs.Empty);
    private void BtnFit_Click(object sender, RoutedEventArgs e) => FitClicked?.Invoke(this, EventArgs.Empty);
    private void BtnSlideshow_Click(object sender, RoutedEventArgs e) => SlideshowClicked?.Invoke(this, EventArgs.Empty);
    private void BtnFullscreen_Click(object sender, RoutedEventArgs e) => FullscreenClicked?.Invoke(this, EventArgs.Empty);
    private void ZoomTextBlock_Click(object sender, MouseButtonEventArgs e) => ZoomTextClicked?.Invoke(this, EventArgs.Empty);
}