using System.Numerics;
using Kryolite.Shared;
using MemoryPack;

namespace Kryolite.Node.Network;

[MemoryPackable]
public partial class NodeInfoResponse
{
    public long CurrentTime { get; init; }
    public long Height { get; init; }
    public BigInteger Weight { get; init; }
    public SHA256Hash LastHash { get; init; } = SHA256Hash.NULL_HASH;
}
