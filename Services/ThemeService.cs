using System;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;

namespace ApertureNeo.Services;

public enum AppTheme
{
    Dark,
    Light,
    System
}

public static class ThemeService
{
    private static AppTheme _current = AppTheme.System;
    public static AppTheme Current => _current;
    public static event Action<AppTheme>? Changed;

    public static void Apply(AppTheme theme)
    {
        _current = theme;
        var effective = theme == AppTheme.System ? DetectSystemTheme() : theme;

        try
        {
            var wpuiTheme = effective == AppTheme.Dark
                ? Wpf.Ui.Appearance.ApplicationTheme.Dark
                : Wpf.Ui.Appearance.ApplicationTheme.Light;
            Wpf.Ui.Appearance.ApplicationThemeManager.Apply(
                wpuiTheme,
                Wpf.Ui.Controls.WindowBackdropType.Mica,
                true);
        }
        catch
        {
        }

        SwapThemeDictionary(effective);
        Changed?.Invoke(theme);
    }

    private static void SwapThemeDictionary(AppTheme effective)
    {
        try
        {
            var app = Application.Current;
            if (app == null) return;

            var path = effective == AppTheme.Dark
                ? "Resources/Themes/Dark.xaml"
                : "Resources/Themes/Light.xaml";

            var newDict = new ResourceDictionary
            {
                Source = new Uri(path, UriKind.Relative)
            };

            var existing = app.Resources.MergedDictionaries
                .Where(d => d.Source?.OriginalString.Contains("/Themes/") == true)
                .ToList();
            foreach (var d in existing) app.Resources.MergedDictionaries.Remove(d);

            app.Resources.MergedDictionaries.Insert(0, newDict);
        }
        catch
        {
        }
    }

    public static AppTheme DetectSystemTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("AppsUseLightTheme") is int v)
                return v == 1 ? AppTheme.Light : AppTheme.Dark;
        }
        catch
        {
        }
        return AppTheme.Dark;
    }

    public static void StartSystemThemeWatcher()
    {
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.General) return;
        if (_current != AppTheme.System) return;

        Application.Current?.Dispatcher.BeginInvoke(new Action(() => Apply(AppTheme.System)),
            DispatcherPriority.Background);
    }
}
