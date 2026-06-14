using System;
using System.Globalization;
using System.Windows.Data;

namespace ApertureNeo.Demo.Converters;

/// <summary>
/// Returns true (a <see cref="bool"/>) if the bound value is a
/// non-empty string, false otherwise. Used in XAML to bind
/// visibility or template triggers to a populated/unpopulated
/// text property (e.g. the info popover's EXIF fields).
/// </summary>
public class StringNotEmptyConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is string s && !string.IsNullOrEmpty(s);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}
