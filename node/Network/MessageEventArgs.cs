using MessagePack;

namespace Marccacoin;

public class MessageEventArgs
{
    public string Hostname { get; set; }
    public Message Message { get; }
    public bool Rebroadcast { get; set; }

    public MessageEventArgs(ArraySegment<byte> data)
    {
        Message = MessagePackSerializer.Deserialize<Message>(data);
    }
}
