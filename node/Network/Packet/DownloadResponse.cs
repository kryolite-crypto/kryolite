using MessagePack;
using Microsoft.Extensions.Logging;
using Kryolite.Shared;
using static Kryolite.Node.NetworkManager;
using Microsoft.Extensions.DependencyInjection;
using Kryolite.Shared.Dto;

namespace Kryolite.Node;

[MessagePackObject]
public class DownloadResponse : IPacket
{
    [Key(0)]
    public List<TransactionDto> Transactions { get; }

    public DownloadResponse(List<TransactionDto> transactions)
    {
        Transactions = transactions ?? throw new ArgumentNullException(nameof(transactions));
    }

    public void Handle(Peer peer, MessageReceivedEventArgs args, IServiceProvider serviceProvider)
    {
        throw new NotSupportedException();
    }
}
