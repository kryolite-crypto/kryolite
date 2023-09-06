using Avalonia.Data.Converters;
using Avalonia.Data;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Kryolite.Shared;

namespace Kryolite.Wallet;

public class AddressConverter : IValueConverter
{
    public static readonly TimestampConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is Address address)
        {
            return address.ToString();
        }

        return new BindingNotification(new InvalidCastException(), BindingErrorType.Error);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
