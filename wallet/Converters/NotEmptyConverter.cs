using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Kryolite.Wallet;

public class NotEmptyConverter : IValueConverter
{
    public static readonly NotEmptyConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string str)
        {
            return !string.IsNullOrWhiteSpace(str);
        }

        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}