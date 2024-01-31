using Kryolite.Shared.Dto;
using MemoryPack;

namespace Kryolite.Node;

[MemoryPackable]
public partial class TransactionResponse : IPacket
{
    public TransactionDto? Transaction { get; set; }

    [MemoryPackConstructor]
    public TransactionResponse(TransactionDto? transaction)
    {
        Transaction = transaction;
    }

    public void Handle(Peer peer, MessageReceivedEventArgs args, IServiceProvider provider)
    {
        throw new NotImplementedException();
    }
}
