using Avalonia;
using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace Kryolite.Wallet;

public class WidthToMarginConverter : IValueConverter
{
    public static readonly WidthToMarginConverter Instance = new();

    public object? Convert(object? value, System.Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not Rect rect)
        {
            return new Thickness(0, 8, 0, 0);
        }

        return new Thickness(rect.Width + 15, 8, 0, 0);
    }

    public object ConvertBack(object? value, System.Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
