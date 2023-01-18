using System.Linq.Expressions;
using Kryolite.Shared;
using Wasmtime;

namespace Kryolite;

public static class Extensions
{
    public static Address ReadAddress(this Memory memory, int address)
    {
        return (Address)memory.GetSpan(address, Address.ADDRESS_SZ);
    }

    public static void WriteBuffer(this Memory memory, int address, byte[] buffer)
    {
        foreach (var b in buffer) 
        {
            memory.WriteByte(address, b);
            address++;
        }
    }

    public static string ToHostname(this Uri uri)
    {
        return uri.ToString().TrimEnd('/');
    }
}
