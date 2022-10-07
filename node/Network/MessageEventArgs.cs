using MessagePack;

namespace Marccacoin;

public class MessageEventArgs
{
    public Message Message { get; }
    public bool Rebroadcast { get; set; }

    public MessageEventArgs(ArraySegment<byte> data)
    {
        Message = MessagePackSerializer.Deserialize<Message>(data);
    }
}
