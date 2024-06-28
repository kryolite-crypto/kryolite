using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace Kryolite.Wallet;

public class SnakeCaseConverter : IValueConverter
{
    public static readonly SnakeCaseConverter Instance = new();

    public object? Convert(object? value, System.Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string str)
        {
            return string.Empty;
        }

        return char.ToUpper(str[0]) + str.Substring(1).Replace('_', ' ');
    }

    public object ConvertBack(object? value, System.Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string str)
        {
            return string.Empty;
        }

        return str.Replace(' ', '_').ToLower();
    }
}
