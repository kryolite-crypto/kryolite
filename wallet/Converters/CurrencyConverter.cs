using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace Kryolite.Wallet;

public class CurrencyConverter : IValueConverter
{
    public static readonly CurrencyConverter Instance = new();

    public object? Convert( object? value, System.Type targetType, object? parameter, CultureInfo culture )
    {
        if (value is ulong uval)
        {
            return String.Format(CultureInfo.InvariantCulture, "{0:0.######} KRYO", uval / 1000000.0);
        }

        if (value is long val)
        {
            return String.Format(CultureInfo.InvariantCulture, "{0:0.######} KRYO", val / 1000000.0);
        }

        // converter used for the wrong type
        return new BindingNotification(new InvalidCastException(), BindingErrorType.Error);
    }

    public object ConvertBack( object? value, System.Type targetType, object? parameter, CultureInfo culture )
    {
        return value!;
    }
}
