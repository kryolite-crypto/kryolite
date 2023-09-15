using Kryolite.Shared.Dto;
using MessagePack;
using NSec.Cryptography;
using System.Collections.Immutable;
using System.Security.Cryptography;

namespace Kryolite.Shared.Blockchain;

[MessagePackObject]
public class Vote : Transaction
{
    public SHA256Hash LastHash { 
        get => Data ?? SHA256Hash.NULL_HASH;
        init => Data = value;
    }

    public Vote()
    {

    }

    public Vote(PublicKey publicKey, SHA256Hash viewId, long stake, ImmutableList<SHA256Hash> parents)
    {
        TransactionType = TransactionType.VOTE;
        PublicKey = publicKey;
        Value = stake;
        Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Parents = parents;
        Data = viewId;
        TransactionId = CalculateHash();
    }

    public Vote(TransactionDto tx, ImmutableList<SHA256Hash> parents)
    {
        TransactionType = TransactionType.VOTE;
        PublicKey = tx.PublicKey ?? throw new Exception("vote requires public key");
        To = tx.To;
        Value = tx.Value;
        Data = tx.Data;
        Timestamp = tx.Timestamp;
        Signature = tx.Signature ?? throw new Exception("vote requires signature");
        Parents = parents.Distinct().ToImmutableList();
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
