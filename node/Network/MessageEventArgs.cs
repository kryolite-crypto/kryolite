using MessagePack;
using WatsonWebsocket;

namespace Kryolite;

public class MessageEventArgs
{
    public Message Message { get; }
    public bool Rebroadcast { get; set; }

    public MessageEventArgs(ArraySegment<byte> data, MessagePackSerializerOptions lz4Options)
    {
        Message = MessagePackSerializer.Deserialize<Message>(data, lz4Options);
    }
}
