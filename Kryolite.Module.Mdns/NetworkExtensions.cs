using System.Net;

namespace Kryolite.Module.Mdns;

public static class Extensions
{
    public static ushort NetworkToHostOrder(this ushort value)
    {
        return (ushort)IPAddress.NetworkToHostOrder((short)value);
    }

    public static ushort HostToNetworkOrder(this ushort value)
    {
        return (ushort)IPAddress.NetworkToHostOrder((short)value);
    }

    public static uint NetworkToHostOrder(this uint value)
    {
        return (uint)IPAddress.NetworkToHostOrder((int)value);
    }

    public static uint HostToNetworkOrder(this uint value)
    {
        return (uint)IPAddress.NetworkToHostOrder((int)value);
    }
}
