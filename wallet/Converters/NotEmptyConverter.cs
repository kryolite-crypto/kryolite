using System;
using System.Collections;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Kryolite.Wallet;

public class NotEmptyConverter : IValueConverter
{
    public static readonly NotEmptyConverter Instance = new();

    public object? Convert(object? value, System.Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string str)
        {
            return !string.IsNullOrWhiteSpace(str);
        }
        else if (value is IList list)
        {
            return list.Count > 0;
        }

        return false;
    }

    public object? ConvertBack(object? value, System.Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}