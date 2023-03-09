using Kryolite.Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace node;

public static class ValueConverter
{
    public static object ConvertToValue(Span<byte> typeBytes, Span<byte> valBytes)
    {
        var type = Encoding.UTF8.GetString(typeBytes)
            .Replace("&", string.Empty);

        switch (type)
        {
            case "Address":
                return ((Address)valBytes).ToString();
            case "bool":
                return BitConverter.ToBoolean(valBytes);
            case "i8":
            case "u8":
                return valBytes[0];
            case "i16":
                return BitConverter.ToInt16(valBytes);
            case "u16":
                return BitConverter.ToUInt16(valBytes);
            case "i32":
                return BitConverter.ToInt32(valBytes);
            case "u32":
                return BitConverter.ToUInt32(valBytes);
            case "i64":
                return BitConverter.ToInt64(valBytes);
            case "u64":
                return BitConverter.ToUInt64(valBytes);
            case "f32":
                return BitConverter.ToSingle(valBytes);
            case "f64":
                return BitConverter.ToDouble(valBytes);
            case "isize":
                if (valBytes.Length == 8)
                {
                    return BitConverter.ToInt64(valBytes);
                }
                else
                {
                    return BitConverter.ToInt32(valBytes);
                }
            case "usize":
                if (valBytes.Length == 8)
                {
                    return BitConverter.ToUInt64(valBytes);
                }
                else
                {
                    return BitConverter.ToUInt32(valBytes);
                }
            case "str":
            case "& str":
                return Encoding.UTF8.GetString(valBytes);
            default:
                return valBytes.ToArray();
        }
    }
}
