using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Teleprompter.App.Services;

/// <summary>Visible when the bound string has content, collapsed when empty.</summary>
public sealed class NonEmptyStringToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
