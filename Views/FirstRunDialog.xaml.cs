using System.Windows;

namespace ApertureNeo.Views;

/// <summary>
/// Lightweight onboarding dialog shown once after the user's
/// first install (or upgrade). Asks whether to enable
/// auto-start + close-to-tray. The user's selections are
/// written to <see cref="Services.ISettingsStore"/> directly
/// and persisted; the registry write for auto-start happens
/// once Stage 7 lands.
/// </summary>
public partial class FirstRunDialog : Window
{
    public bool EnableAutoStart { get; private set; } = true;
    public bool EnableCloseToTray { get; private set; } = true;

    public FirstRunDialog()
    {
        InitializeComponent();
    }

    private void OkBtn_Click(object sender, RoutedEventArgs e)
    {
        EnableAutoStart = AutoStartCheck.IsChecked == true;
        EnableCloseToTray = CloseToTrayCheck.IsChecked == true;
        DialogResult = true;
        Close();
    }

    private void LaterBtn_Click(object sender, RoutedEventArgs e)
    {
        // Skip: persist default values (both on) but don't
        // auto-start this session. The user can change later
        // via the settings panel.
        EnableAutoStart = AutoStartCheck.IsChecked == true;
        EnableCloseToTray = CloseToTrayCheck.IsChecked == true;
        DialogResult = false;
        Close();
    }
}