using System.Runtime.CompilerServices;

namespace Kryolite.ByteSerializer;

public class AddressOutOfRangeException : ArgumentOutOfRangeException
{
    public static void ThrowIfAddressIsGreaterThan(ref byte left, ref byte right)
    {
        if (Unsafe.IsAddressGreaterThan(ref left, ref right))
        {
            throw new AddressOutOfRangeException();
        }
    }
}