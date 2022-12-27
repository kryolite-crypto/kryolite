using MessagePack;

namespace Kryolite;

public class MessageEventArgs
{
    public string Hostname { get; set; }
    public Message Message { get; }
    public bool Rebroadcast { get; set; }

    public MessageEventArgs(ArraySegment<byte> data, MessagePackSerializerOptions lz4Options)
    {
        Message = MessagePackSerializer.Deserialize<Message>(data, lz4Options);
    }
}
