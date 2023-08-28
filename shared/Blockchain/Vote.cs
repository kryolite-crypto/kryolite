using Kryolite.Shared.Dto;
using MessagePack;
using NSec.Cryptography;
using System.Security.Cryptography;

namespace Kryolite.Shared.Blockchain;

[MessagePackObject]
public class Vote : Transaction
{
    public SHA256Hash LastHash { get => Data ?? SHA256Hash.NULL_HASH; set => Data = value; }

    public Vote()
    {

    }

    public Vote(PublicKey publicKey, SHA256Hash viewId, List<SHA256Hash> parents)
    {
        TransactionType = TransactionType.VOTE;
        PublicKey = publicKey;
        Value = 0;
        Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Parents = parents;
        Data = viewId;
        TransactionId = CalculateHash();
    }

    public Vote(TransactionDto tx, List<SHA256Hash> parents)
    {
        TransactionType = TransactionType.VOTE;
        PublicKey = tx.PublicKey ?? throw new Exception("vote requires public key");
        To = tx.To;
        Value = tx.Value;
        Data = tx.Data;
        Timestamp = tx.Timestamp;
        Signature = tx.Signature ?? throw new Exception("vote requires signature");
        Parents = parents;
        TransactionId = CalculateHash();
    }

    public Vote(Transaction tx)
    {
        Id = tx.Id;
        TransactionId = tx.TransactionId;
        Height = tx.Height;
        TransactionType = tx.TransactionType;
        PublicKey = tx.PublicKey ?? throw new Exception("vote requires public key");
        To = tx.To;
        Value = tx.Value;
        Data = tx.Data;
        Timestamp = tx.Timestamp;
        Signature = tx.Signature ?? throw new Exception("vote requires signature");
        ExecutionResult = tx.ExecutionResult;
        Parents = tx.Parents;
        Effects = tx.Effects;
    }
}
