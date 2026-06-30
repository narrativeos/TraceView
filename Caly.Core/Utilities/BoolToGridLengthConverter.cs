using Avalonia.Controls;
using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace Caly.Core.Utilities;

/// <summary>
/// Converts a boolean value to a GridLength.
/// When true, returns Star (1*). When false, returns Auto.
/// </summary>
public class BoolToGridLengthConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isVisible)
        {
            return isVisible ? GridLength.Star : GridLength.Auto;
        }
        return GridLength.Auto;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
