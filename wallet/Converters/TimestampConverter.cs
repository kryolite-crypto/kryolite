using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace Kryolite.Wallet;

public class TimestampConverter : IValueConverter
{
    public static readonly TimestampConverter Instance = new();

    public object? Convert( object? value, System.Type targetType, object? parameter, CultureInfo culture )
    {
        if (value is long v)
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(v).ToLocalTime().ToString().Split("+")[0];
        }
        else if (value is DateTimeOffset dt)
        {
            return dt.ToLocalTime().ToString().Split("+")[0];
        }
        // converter used for the wrong type
        return new BindingNotification(new InvalidCastException(), BindingErrorType.Error);
    }

    public object ConvertBack( object? value, System.Type targetType, object? parameter, CultureInfo culture )
    {
        throw new NotImplementedException();
    }
}
