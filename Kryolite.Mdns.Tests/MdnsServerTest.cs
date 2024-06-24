using System.Net;

namespace Kryolite.Mdns.Tests;

public class MdnsServerTest
{
    [Fact]
    public async Task MdnsServer_ShouldReply()
    {
        using var cts = new CancellationTokenSource();
        using var server = new MdnsServer("_rpc._kryolite._tcp.local", IPAddress.Parse("127.0.0.1"), 11611);
        using var client = new MdnsClient();

        server.StartListening(cts.Token);

        var endpoints = await client.Query("_rpc._kryolite._tcp.local");

        Assert.Single(endpoints);
        Assert.Equal("http://127.0.0.1:11611", endpoints[0]);
    }
}
