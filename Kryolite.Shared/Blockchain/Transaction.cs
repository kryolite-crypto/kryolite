using System.Security.Cryptography;
using Kryolite.EventBus;
using Kryolite.Shared.Dto;
using NSec.Cryptography;

namespace Kryolite.Shared.Blockchain;

public sealed class Transaction : EventBase, IComparable<Transaction>, ISerializable
{
    public long Id;
    public TransactionType TransactionType;
    public PublicKey PublicKey;
    public Address To;
    public ulong Value;
    public byte[] Data;
    public long Timestamp;
    public Signature Signature;
    public ExecutionResult ExecutionResult;
    public List<Effect> Effects = [];

    public Address From => PublicKey.ToAddress();

    public Transaction()
    {
        PublicKey = new();
        To = new();
        Signature = new();
        Data = [];
    }

    public Transaction(TransactionDto tx)
    {
        TransactionType = tx.TransactionType;
        PublicKey = tx.PublicKey ?? throw new Exception("transaction requires public key");
        To = tx.To;
        Value = tx.Value;
        Data = tx.Data;
        Timestamp = tx.Timestamp;
        Signature = tx.Signature ?? throw new Exception("transaction requires signature");
    }

    public void Sign(PrivateKey privateKey)
    {
        var algorithm = new Ed25519();

        using var key = Key.Import(algorithm, (ReadOnlySpan<byte>)privateKey, KeyBlobFormat.RawPrivateKey);
        using var stream = new MemoryStream();

        stream.WriteByte((byte)TransactionType);
        stream.Write((ReadOnlySpan<byte>)PublicKey);
        stream.Write((ReadOnlySpan<byte>)To);
        stream.Write(BitConverter.GetBytes(Value));
        stream.Write(Data);
        stream.Write(BitConverter.GetBytes(Timestamp));
        stream.Flush();

        Signature = (Signature)algorithm.Sign(key, stream.ToArray());
    }

    public bool Verify()
    {
        var algorithm = new Ed25519();
        using var stream = new MemoryStream();

        stream.WriteByte((byte)TransactionType);
        stream.Write((ReadOnlySpan<byte>)PublicKey);
        stream.Write((ReadOnlySpan<byte>)To);
        stream.Write(BitConverter.GetBytes(Value));
        stream.Write(Data);
        stream.Write(BitConverter.GetBytes(Timestamp));
        stream.Flush();

        var key = NSec.Cryptography.PublicKey.Import(algorithm, (ReadOnlySpan<byte>)PublicKey, KeyBlobFormat.RawPublicKey);
        
        return algorithm.Verify(key, stream.ToArray(), (ReadOnlySpan<byte>)Signature);
    }

    public SHA256Hash CalculateHash()
    {
        using var sha256 = SHA256.Create();
        using var stream = new MemoryStream();

        stream.WriteByte((byte)TransactionType);

        if (PublicKey is not null)
        {
            stream.Write((ReadOnlySpan<byte>)PublicKey);
        }

        stream.Write((ReadOnlySpan<byte>)To);
        stream.Write(BitConverter.GetBytes(Value));
        stream.Write(Data);
        stream.Write(BitConverter.GetBytes(Timestamp));
        stream.Flush();
        stream.Position = 0;

        return sha256.ComputeHash(stream);
    }

    public int CompareTo(Transaction? other)
    {
        return MemoryExtensions.SequenceCompareTo((ReadOnlySpan<byte>)CalculateHash(), (ReadOnlySpan<byte>)(other?.CalculateHash() ?? new byte[0]));
    }

    public byte GetSerializerId()
    {
        return (byte)SerializerEnum.TRANSACTION;
    }

    public int GetLength() =>
        Serializer.SizeOf(Id) +
        Serializer.SizeOf(TransactionType) +
        Serializer.SizeOf(Value) +
        Serializer.SizeOf(Timestamp) +
        Serializer.SizeOf(PublicKey) +
        Serializer.SizeOf(To) +
        Serializer.SizeOf(Signature) +
        Serializer.SizeOf(ExecutionResult) +
        Serializer.SizeOf(Data) +
        Serializer.SizeOf(Effects);

    public unsafe void Serialize(ref Serializer serializer)
    {
        serializer.Write(Id);
        serializer.Write(TransactionType);
        serializer.Write(Value);
        serializer.Write(Timestamp);
        serializer.Write(PublicKey);
        serializer.Write(To);
        serializer.Write(Signature);
        serializer.Write(ExecutionResult);
        serializer.Write(Data);
        serializer.Write(Effects);
    }

    public void Deserialize(ref Serializer serializer)
    {
        serializer.Read(ref Id);
        serializer.Read(ref TransactionType);
        serializer.Read(ref Value);
        serializer.Read(ref Timestamp);
        serializer.Read(ref PublicKey);
        serializer.Read(ref To);
        serializer.Read(ref Signature);
        serializer.Read(ref ExecutionResult);
        serializer.Read(ref Data);
        serializer.Read(ref Effects, () => new Effect());
    }
}
