using Kryolite.ByteSerializer;
using Kryolite.Shared;

namespace Kryolite.Grpc.NodeService;

public class BatchBroadcast : ISerializable
{
    public byte[][] Messages;

    public BatchBroadcast()
    {
        Messages = [];
    }

    public BatchBroadcast(byte[][] value)
    {
        Messages = value;
    }

    public byte GetSerializerId()
    {
        return (byte)SerializerEnum.BATCH_BROADCAST;
    }

    public int GetLength()
    {
        var length = sizeof(int);

        for (var i = 0; i < Messages.Length; i++)
        {
            length += Serializer.SizeOf(Messages[i]);
        }

        return length;
    }

    public void Serialize(ref Serializer serializer)
    {
        serializer.Write(Messages.Length);

        for (var i = 0; i < Messages.Length; i++)
        {
            serializer.Write(Messages[i]);
        }
    }

    public void Deserialize(ref Serializer serializer)
    {
        var length = 0;
        serializer.Read(ref length);

        Messages = new byte[length][];

        for (var i = 0; i < length; i++)
        {
            Messages[i] = [];
            serializer.Read(ref Messages[i]);
        }
    }
}

public class BatchForward : ISerializable
{
    public PublicKey PublicKey;
    public BatchBroadcast Batch;

    public BatchForward()
    {
        PublicKey = new();
        Batch = new();
    }

    public BatchForward(PublicKey publicKey, BatchBroadcast batch)
    {
        PublicKey = publicKey;
        Batch = batch;
    }

    public byte GetSerializerId()
    {
        return (byte)SerializerEnum.BATCH_FORWARD;
    }

    public int GetLength() =>
        Serializer.SizeOf(PublicKey) +
        Serializer.SizeOf(Batch);

    public void Serialize(ref Serializer serializer)
    {
        serializer.Write(PublicKey);
        serializer.Write(Batch);
    }

    public void Deserialize(ref Serializer serializer)
    {
        serializer.Read(ref PublicKey);
        serializer.Read(ref Batch);
    }
}