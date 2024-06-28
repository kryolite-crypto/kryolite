using Avalonia.Data.Converters;
using Avalonia.Data;
using System;
using System.Globalization;
using Kryolite.Shared;
using Kryolite.Type;

namespace Kryolite.Wallet;

public class AddressConverter : IValueConverter
{
    public static readonly TimestampConverter Instance = new();

    public object? Convert(object? value, System.Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is Address address)
        {
            return address.ToString();
        }

        return new BindingNotification(new InvalidCastException(), BindingErrorType.Error);
    }

    public object ConvertBack(object? value, System.Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
