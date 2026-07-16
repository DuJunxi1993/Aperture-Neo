using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ApertureNeo.Converters;

/// <summary>
/// Maps <c>bool</c> to <see cref="Visibility"/> for VM-to-View
/// bindings. <c>true</c> → <see cref="Visibility.Visible"/>,
/// <c>false</c> → <see cref="Visibility.Collapsed"/>.
/// P2: registered globally in App.xaml as
/// <c>BoolToVisibility</c> so XAML can write
/// <c>Visibility="{Binding IsXxx, Converter={StaticResource BoolToVisibility}}</c>.
/// </summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is bool b && b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is Visibility v && v == Visibility.Visible;
    }
}