namespace Kryolite.Node;

public interface IPacket
{
    public Task Handle(Peer peer, MessageEventArgs args, PacketContext context);
}
