using Marccacoin.Shared;
using MessagePack;
using MessagePack.Formatters;

namespace Marccacoin;

[MessagePackObject]
public class QueryNodeInfo
{
    [Key(0)]
    public int Port { get; set; }
}

[MessagePackObject]
public class RequestChainSync
{
    [Key(0)]
    public long StartBlock { get; init; }
    [Key(1)]
    public byte[]? StartHash { get; init; }
}
