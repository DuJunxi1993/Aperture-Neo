using System.Windows;
using System.Windows.Input;
using Wpf.Ui.Controls;

namespace ApertureNeo;

/// <summary>
/// "About" dialog. Modal, owner-relative, no business logic beyond
/// "OK closes the window" and "double-click the title bar closes".
/// </summary>
public partial class AboutWindow : FluentWindow
{
    public AboutWindow(Window owner)
    {
        InitializeComponent();
        Owner = owner;
    }

    private void BtnOk_Click(object sender, RoutedEventArgs e) => Close();
    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) { Close(); return; }
        DragMove();
    }
}
