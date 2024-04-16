using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Kryolite.Node.Repository;
using Kryolite.ByteSerializer;
using Kryolite.Transport.Websocket;
using Microsoft.Extensions.Hosting;

namespace Kryolite.Node.Network;

public class NodeBroadcast : IBroadcast
{
    public AuthRequest AuthRequest;
    public string Uri;

    public NodeBroadcast()
    {
        AuthRequest = new();
        Uri = string.Empty;
    }

    public NodeBroadcast(AuthRequest authRequest, string uri)
    {
        AuthRequest = authRequest;
        Uri = uri;
    }

    public Task Handle(Node node, IServiceProvider provider)
    {
        using var scope = provider.CreateScope();

        var keyRepository = provider.GetRequiredService<IKeyRepository>();
        var key = keyRepository.GetPublicKey();

        if (AuthRequest.PublicKey == key)
        {
            return Task.CompletedTask;
        }

        if (!AuthRequest.Verify())
        {
            return Task.CompletedTask;
        }

        var nodeTable = scope.ServiceProvider.GetRequiredService<NodeTable>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<NodeBroadcast>>();
        var lifetime = scope.ServiceProvider.GetRequiredService<IHostApplicationLifetime>();

        logger.LogDebug("Received NodeBroadcast from {hostname}", node.Uri.ToHostname());

        var exists = nodeTable.GetNode(AuthRequest.PublicKey);

        if (node is null)
        {
            var uri = new Uri(Uri);
            nodeTable.AddNode(AuthRequest.PublicKey, uri, WebsocketChannel.ForAddress(uri, lifetime.ApplicationStopping));

            // Rebroadcast
            BroadcastManager.Broadcast(this);
        }

        return Task.CompletedTask;
    }

    public byte GetSerializerId()
    {
        return (byte)SerializerEnum.NODE_BROADCAST;
    }

    public int GetLength() =>
        Serializer.SizeOf(AuthRequest) +
        Serializer.SizeOf(Uri);

    public void Serialize(ref Serializer serializer)
    {
        serializer.Write(AuthRequest);
        serializer.Write(Uri);
    }

    public void Deserialize(ref Serializer serializer)
    {
        serializer.Read(ref AuthRequest);
        serializer.Read(ref Uri);
    }
}
