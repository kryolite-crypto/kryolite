using Avalonia.Data.Converters;
using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Kryolite.Wallet;

public class CapitalToSentenceConverter : IValueConverter
{
    public static readonly CapitalToSentenceConverter Instance = new();

    public object? Convert(object? value, System.Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string str)
        {
            return string.Empty;
        }

        return Regex.Replace(str, @"([a-z])([A-Z])", "$1 $2");
    }

    public object ConvertBack(object? value, System.Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
