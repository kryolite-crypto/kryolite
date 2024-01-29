using Kryolite.Shared;
using System;
using System.Collections.Generic;
using System.Data.SqlTypes;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Kryolite.Node;

public static class ValueConverter
{
    public static object ConvertToValue(Span<byte> typeBytes, Span<byte> valBytes)
    {
        var cleaned_type = Encoding.UTF8.GetString(typeBytes)
            .Replace("&", string.Empty);

        switch (cleaned_type)
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
            case "isize":
                return BitConverter.ToInt32(valBytes);
            case "u32":
            case "usize":
                return BitConverter.ToUInt32(valBytes);
            case "i64":
                return BitConverter.ToInt64(valBytes);
            case "u64":
                return BitConverter.ToUInt64(valBytes);
            case "f32":
                return BitConverter.ToSingle(valBytes);
            case "f64":
                return BitConverter.ToDouble(valBytes);
            case "str":
            case "& str":
                return Encoding.UTF8.GetString(valBytes);
            default:
                return valBytes.ToArray();
        }
    }

    public static object ConvertFromValue(string type, object value)
    {
        if (value.GetType().IsPrimitive)
        {
            // TODO: Do we need to limit some primitives?
            return value;
        }

        switch (value)
        {
            case string str:
                return ConvertFromString(type, str);
            case byte[] bytes:
                return bytes;
            case Address addr:
                return addr.Buffer;
            case SHA256Hash hash:
                return hash.Buffer;
            default:
                throw new ArgumentException("Unknown argument type: " + value.GetType());
        }
    }

    public static object ConvertFromString(string type, string str)
    {
        switch (type)
        {
            case "Address":
                return ((Address)str).Buffer;
            case "U256":
                return ((SHA256Hash)str).Buffer;
            case "bool":
                return bool.Parse(str);
            case "i8":
            case "u8":
            case "byte":
            case "sbyte":
                return byte.Parse(str);
            case "i16":
            case "short":
                return short.Parse(str);
            case "u16":
            case "ushort":
                return ushort.Parse(str);
            case "i32":
            case "isize":
            case "int":
                return int.Parse(str);
            case "u32":
            case "usize":
            case "uint":
                return uint.Parse(str);
            case "i64":
            case "long":
                return long.Parse(str);
            case "u64":
            case "ulong":
                return ulong.Parse(str);
            case "f32":
            case "float":
                return float.Parse(str);
            case "f64":
            case "double":
                return double.Parse(str);
            default:
                return Encoding.UTF8.GetBytes(str);
        }
    }
}
