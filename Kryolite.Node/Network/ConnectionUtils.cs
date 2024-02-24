using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;

namespace Kryolite.Node.Network;

public static class UriFormatter
{
    public static bool TryParse(string? url, [NotNullWhen(true)] out Uri? uri)
    {
        if (url is null)
        {
            uri = null;
            return false;
        }

        try
        {
            var builder = new UriBuilder(url);
            uri = builder.Uri;

            return true;
        }
        catch (Exception)
        {
            uri = null;
            return false;
        }
    }
}