using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ApertureNeo.Converters;

/// <summary>
/// Inverse of <see cref="BoolToVisibilityConverter"/>.
/// <c>true</c> → <see cref="Visibility.Collapsed"/>,
/// <c>false</c> → <see cref="Visibility.Visible"/>. Used
/// when a UI element should hide while a flag is set (e.g. the
/// image info pill hides while annotation mode is active so
/// the annotation toolbar isn't visually crowded). Stage C.
/// </summary>
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is bool b && b ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is Visibility v && v == Visibility.Collapsed;
    }
}