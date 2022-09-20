using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace holvi_wallet;

public class TimestampConverter : IValueConverter
{
    public static readonly TimestampConverter Instance = new();

    public object? Convert( object? value, Type targetType, object? parameter, CultureInfo culture )
    {
        if (value is long)
        {
            return new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc)
                .AddSeconds((long)value).ToLocalTime().ToString();
        }
        // converter used for the wrong type
        return new BindingNotification(new InvalidCastException(), BindingErrorType.Error);
    }

    public object ConvertBack( object? value, Type targetType, object? parameter, CultureInfo culture )
    {
        throw new NotImplementedException();
    }
}
