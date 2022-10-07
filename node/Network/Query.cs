using Marccacoin.Shared;
using MessagePack;
using MessagePack.Formatters;

namespace Marccacoin;

[MessagePackObject]
public class Query
{
    [Key(0)]
    public QueryType QueryType { get; init; }
    [Key(1)]
    [MessagePackFormatter(typeof(TypelessFormatter))]
    public object? Params { get; set; }
}

public enum QueryType
{
    NODE_INFO,
    CHAIN_SYNC
}

[MessagePackObject]
public class ChainSyncParams
{
    [Key(0)]
    public long StartBlock { get; init; }
    [Key(1)]
    public byte[]? StartHash { get; init; }
}
