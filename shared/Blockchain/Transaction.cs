using System.Security.Cryptography;
using System.Text.Json.Serialization;
using DuckDB.NET.Data;
using Kryolite.Shared.Dto;
using MessagePack;
using NSec.Cryptography;

namespace Kryolite.Shared.Blockchain;

public class Transaction : IComparable<Transaction>
{
    public SHA256Hash TransactionId { get; set; } = new SHA256Hash();
    public long? Height { get; set; }

    public TransactionType TransactionType { get; set; }
    public virtual PublicKey? PublicKey { get; set; }
    public virtual Address? To { get; set; } = new Address();
    public ulong Value { get; set; }
    public SHA256Hash Pow { get; set; } = new SHA256Hash();
    public byte[]? Data { get; set; }
    public long Timestamp { get; set; }
    public virtual Signature? Signature { get; set; }
    public ExecutionResult ExecutionResult { get; set; }
    public List<SHA256Hash> Parents { get; set; } = new List<SHA256Hash>();

    public List<Effect> Effects { get; set; } = new();

    public Address From
    {
        get => PublicKey?.ToAddress() ?? new Address();
        private set { }
    }

    public Transaction()
    {

    }

    public Transaction(TransactionDto tx, List<SHA256Hash> parents)
    {
        TransactionType = tx.TransactionType;
        PublicKey = tx.PublicKey ?? throw new Exception("payment requires public key");
        To = tx.To;
        Value = tx.Value;
        Pow = tx.Pow ?? new SHA256Hash();
        Data = tx.Data;
        Timestamp = tx.Timestamp;
        Signature = tx.Signature ?? throw new Exception("payment requires signature");
        Parents = parents;
        TransactionId = CalculateHash();
    }

    public virtual void Sign(PrivateKey privateKey)
    {
        var algorithm = SignatureAlgorithm.Ed25519;

        using var key = Key.Import(algorithm, privateKey, KeyBlobFormat.RawPrivateKey);
        using var stream = new MemoryStream();

        stream.WriteByte((byte)TransactionType);
        stream.Write(PublicKey ?? throw new Exception("public key required when signing transactions"));

        if (Parents.Count < 2)
        {
            throw new Exception("parent hashes not loaded for transaction");
        }

        if (To is not null)
        {
            stream.Write(To);
        }
        
        stream.Write(BitConverter.GetBytes(Value));
        stream.Write(Data);
        stream.Write(BitConverter.GetBytes(Timestamp));

        foreach (var hash in Parents.Order())
        {
            stream.Write(hash);
        }

        stream.Flush();

        Signature = algorithm.Sign(key, stream.ToArray());
    }

    public virtual bool Verify()
    {
        var algorithm = SignatureAlgorithm.Ed25519;

        using var stream = new MemoryStream();

        stream.WriteByte((byte)TransactionType);
        stream.Write(PublicKey ?? throw new Exception("public key required when verifying signed transaction (malformed transaction?)"));
        
        if (To is not null)
        {
            stream.Write(To);
        }

        stream.Write(BitConverter.GetBytes(Value));
        stream.Write(Data);
        stream.Write(BitConverter.GetBytes(Timestamp));

        if (Parents.Count < 2)
        {
            throw new Exception("parent hashes not loaded for transaction");
        }

        foreach (var hash in Parents.Order())
        {
            stream.Write(hash);
        }

        stream.Flush();

        var key = NSec.Cryptography.PublicKey.Import(SignatureAlgorithm.Ed25519, PublicKey, KeyBlobFormat.RawPublicKey);
        return algorithm.Verify(key, stream.ToArray(), Signature ?? throw new Exception("trying to verify null signature"));
    }

    public virtual SHA256Hash CalculateHash()
    {
        using var sha256 = SHA256.Create();
        using var stream = new MemoryStream();

        stream.WriteByte((byte)TransactionType);

        if (TransactionType == TransactionType.PAYMENT || TransactionType == TransactionType.CONTRACT)
        {
            stream.Write(PublicKey ?? throw new Exception("public key required when hashing payment"));
            stream.Write(Signature ?? throw new Exception("signature required when hashing payment"));
        }

        if (To is not null)
        {
            stream.Write(To);
        }

        stream.Write(BitConverter.GetBytes(Value));
        stream.Write(Data);
        stream.Write(BitConverter.GetBytes(Timestamp));
        stream.Write(Pow);

        if (Parents.Count < 2)
        {
            throw new Exception("parent hashes not loaded for transaction");
        }

        foreach (var hash in Parents.Order())
        {
            stream.Write(hash);
        }

        stream.Flush();
        stream.Position = 0;

        return sha256.ComputeHash(stream);
    }

    public int CompareTo(Transaction? other)
    {
        return MemoryExtensions.SequenceCompareTo((ReadOnlySpan<byte>)TransactionId.Buffer, (ReadOnlySpan<byte>)(other?.TransactionId.Buffer ?? new byte[0]));
    }

    public static Transaction Read(DuckDBDataReader reader)
    {
        var tx = new Transaction();

        tx.TransactionId = reader.GetString(0);
        tx.TransactionType = (TransactionType)reader.GetByte(1);
        if (!reader.IsDBNull(2))
            tx.Height = reader.GetInt64(2);
        if (!reader.IsDBNull(3))
            tx.PublicKey = reader.GetString(3);
        if (!reader.IsDBNull(4))
            tx.To = reader.GetString(4);
        tx.Value = (ulong)reader.GetInt64(5);
        if (!reader.IsDBNull(6))
            tx.Pow = reader.GetString(6);
        if (!reader.IsDBNull(7))
        {
            using var ms = new MemoryStream();
            reader.GetStream(7).CopyTo(ms);
            tx.Data = ms.ToArray();
        }
        tx.Timestamp = reader.GetInt64(8);
        if (!reader.IsDBNull(9))
        {
            tx.Signature = reader.GetString(9);
        }
        tx.ExecutionResult = (ExecutionResult)reader.GetByte(10);

        return tx;
    }
}
