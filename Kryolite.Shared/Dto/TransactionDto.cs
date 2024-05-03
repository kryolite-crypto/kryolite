using Kryolite.ByteSerializer;
using Kryolite.EventBus;
using Kryolite.Shared.Blockchain;
using System.Security.Cryptography;

namespace Kryolite.Shared.Dto;

public class TransactionDto : EventBase, ISerializable
{
    public TransactionType TransactionType;
    public PublicKey PublicKey;
    public Address To;
    public ulong Value;
    public uint MaxFee;
    public byte[] Data;
    public long Timestamp;
    public Signature Signature;

    public TransactionDto()
    {
        PublicKey = new();
        To = new();
        Data = [];
        Signature = new();
    }

    public TransactionDto(Transaction tx)
    {
        TransactionType = tx.TransactionType;
        PublicKey = tx.PublicKey;
        To = tx.To;
        Value = tx.Value;
        MaxFee = tx.MaxFee;
        Data = tx.Data;
        Timestamp = tx.Timestamp;
        Signature = tx.Signature;
    }

    public SHA256Hash CalculateHash()
    {
        using var sha256 = SHA256.Create();
        using var stream = new MemoryStream();

        stream.WriteByte((byte)TransactionType);
        stream.Write(PublicKey);
        stream.Write(To);
        stream.Write(BitConverter.GetBytes(Value));
        stream.Write(BitConverter.GetBytes(MaxFee));
        stream.Write(Data);
        stream.Write(BitConverter.GetBytes(Timestamp));
        stream.Flush();
        stream.Position = 0;

        return sha256.ComputeHash(stream.ToArray());
    }

    public byte GetSerializerId()
    {
        return (byte)SerializerEnum.TRANSACTION_DTO;
    }

    public int GetLength() =>
        Serializer.SizeOf(TransactionType) +
        Serializer.SizeOf(PublicKey) +
        Serializer.SizeOf(To) +
        Serializer.SizeOf(Value) +
        Serializer.SizeOf(MaxFee) +
        Serializer.SizeOf(Timestamp) +
        Serializer.SizeOf(Signature) +
        Serializer.SizeOf(Data);

    public void Serialize(ref Serializer serializer)
    {
        serializer.Write(TransactionType);
        serializer.Write(PublicKey);
        serializer.Write(To);
        serializer.Write(Value);
        serializer.Write(MaxFee);
        serializer.Write(Timestamp);
        serializer.Write(Signature);
        serializer.Write(Data);
    }

    public void Deserialize(ref Serializer serializer)
    {
        serializer.Read(ref TransactionType);
        serializer.Read(ref PublicKey);
        serializer.Read(ref To);
        serializer.Read(ref Value);
        serializer.Read(ref MaxFee);
        serializer.Read(ref Timestamp);
        serializer.Read(ref Signature);
        serializer.Read(ref Data);
    }
}
