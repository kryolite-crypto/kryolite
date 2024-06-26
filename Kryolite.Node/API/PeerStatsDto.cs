using Kryolite.Node.Network;
using Kryolite.Shared;
using Kryolite.Type;

namespace Kryolite.Node.API;

public class PeerStatsDto(NodeConnection connection)
{
    public Uri Uri { get; set; } = connection.Node.Uri;
    public PublicKey PublicKey { get; set; } = connection.Node.PublicKey;
    public DateTime FirstSeen { get; set; } = connection.Node.FirstSeen;
    public DateTime LastSeen { get; set; } = connection.Node.LastSeen;
    public bool IsConnected { get; set; } = connection.Channel.IsConnected;
    public DateTime ConnectedSince { get; set; } = connection.Channel.ConnectedSince;
    public long BytesSent { get; set; } = connection.Channel.BytesSent;
    public long BytesReceived { get; set; } = connection.Channel.BytesReceived;
    public long MessagesSent { get; set; } = connection.Channel.MessagesSent;
    public long MessagesReceived { get; set; } = connection.Channel.MessagesReceived;
    public int FailedConnections { get; set; } = connection.Node.FailedConnections;
    public bool IsSyncInProgress { get; set; } = connection.Node.IsSyncInProgress;
    public bool IsForked { get; set; } = connection.Node.IsForked;
}
