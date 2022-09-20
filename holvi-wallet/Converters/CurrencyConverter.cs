using System;
using System.Globalization;
using System.Linq;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Marccacoin.Shared;

namespace holvi_wallet;

public class CurrencyConverter : IValueConverter
{
    public static readonly CurrencyConverter Instance = new();

    public object? Convert( object? value, Type targetType, object? parameter, CultureInfo culture )
    {
        if (value is ulong uval)
        {
            return String.Format(CultureInfo.InvariantCulture, "{0:0.######} FIMC", uval / 1000000.0);
        }

        if (value is long val)
        {
            return String.Format(CultureInfo.InvariantCulture, "{0:0.######} FIMC", val / 1000000.0);
        }

        // converter used for the wrong type
        return new BindingNotification(new InvalidCastException(), BindingErrorType.Error);
    }

    public object ConvertBack( object? value, Type targetType, object? parameter, CultureInfo culture )
    {
        return value!;
    }
}
