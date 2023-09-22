using Kryolite.Shared.Dto;
using MessagePack;

namespace Kryolite.Node;

[MessagePackObject]
public class TransactionResponse : IPacket
{
    [Key(0)]
    public TransactionDto? Transaction { get; }

    public TransactionResponse(TransactionDto? transaction)
    {
        Transaction = transaction;
    }

    public void Handle(Peer peer, MessageReceivedEventArgs args, IServiceProvider provider)
    {
        throw new NotImplementedException();
    }
}
