using MessagePack;

namespace Kryolite;

[MessagePackObject]
public class QueryNodeInfo
{

}

[MessagePackObject]
public class RequestChainSync
{
    [Key(0)]
    public long StartBlock { get; init; }
    [Key(1)]
    public byte[]? StartHash { get; init; }
}

[MessagePackObject]
public class NodeDiscovery
{

}