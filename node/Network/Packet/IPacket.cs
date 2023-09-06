namespace Kryolite.Node;

public interface IPacket
{
    public void Handle(Peer peer, MessageReceivedEventArgs args, IServiceProvider provider);
}
