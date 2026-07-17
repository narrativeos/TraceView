using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace Caly.Core.Utilities;

/// <summary>
/// Converts a value to true if it equals the parameter, false otherwise.
/// </summary>
public class EqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return parameter != null && Equals(value, parameter);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts a value to true if it does not equal the parameter, false otherwise.
/// </summary>
public class NotEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return !Equals(value, parameter);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts a value to true if it is not null, false otherwise.
/// </summary>
public class IsNotNullConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value != null;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts a value to true if it is null, false otherwise.
/// </summary>
public class IsNullConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value == null;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Inverts a boolean value.
/// </summary>
public class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
            return !boolValue;
        return value == null;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts to true if value > parameter, false otherwise. Supports int, double, etc.
/// </summary>
public class IsGreaterThanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null || parameter == null)
            return false;

        // Handle numeric types properly by converting to double for comparison
        try
        {
            double valueNum = value switch
            {
                int i => i,
                long l => l,
                double d => d,
                float f => f,
                short s => s,
                byte b => b,
                uint ui => ui,
                ulong ul => ul,
                _ => double.TryParse(value.ToString(), out var parsed) ? parsed : double.NaN
            };

            double paramNum = parameter switch
            {
                int i => i,
                long l => l,
                double d => d,
                float f => f,
                short s => s,
                byte b => b,
                string s => double.TryParse(s, out var parsedStr) ? parsedStr : double.NaN,
                _ => double.TryParse(parameter.ToString(), out var parsedOther) ? parsedOther : double.NaN
            };

            if (double.IsNaN(valueNum) || double.IsNaN(paramNum))
                return false;

            return valueNum > paramNum;
        }
        catch
        {
            return false;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
