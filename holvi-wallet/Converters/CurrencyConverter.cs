using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace holvi_wallet;

public class CurrencyConverter : IValueConverter
{
    public static readonly CurrencyConverter Instance = new();

    public object? Convert( object? value, Type targetType, object? parameter, CultureInfo culture )
    {
        if (value is ulong || value is long)
        {
            return String.Format(CultureInfo.InvariantCulture, "{0:0.######} FIMC", ((ulong)value) / 1000000.0);
        }
        // converter used for the wrong type
        return new BindingNotification(new InvalidCastException(), BindingErrorType.Error);
        
    }

    public object ConvertBack( object? value, Type targetType, object? parameter, CultureInfo culture )
    {
        if (value is string) 
        {
            return (ulong)double.Parse(((string)value).Split(" ")[0]) * 1000000;
        }

        // converter used for the wrong type
        return new BindingNotification(new InvalidCastException(), BindingErrorType.Error);
    }
}
