using Microsoft.Extensions.Logging;
using Kryolite.Shared;
using Microsoft.Extensions.DependencyInjection;
using MemoryPack;
using Kryolite.Node.Repository;

namespace Kryolite.Node.Network;

[MemoryPackable]
public partial class NodeBroadcast : IBroadcast
{
    public AuthRequest AuthRequest { get; set; }
    public string Uri { get; set; }

    [MemoryPackConstructor]
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

        logger.LogDebug("Received NodeBroadcast from {hostname}", node.Uri.ToHostname());

        var exists = nodeTable.GetNode(AuthRequest.PublicKey);

        if (node is null)
        {
            nodeTable.AddNode(AuthRequest.PublicKey, new Uri(Uri));

            // Rebroadcast
            BroadcastManager.Broadcast(this);
        }

        return Task.CompletedTask;
    }
}
