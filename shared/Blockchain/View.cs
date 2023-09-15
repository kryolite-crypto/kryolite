using Kryolite.Shared.Dto;
using MessagePack;
using NSec.Cryptography;
using System.Collections.Immutable;
using System.Security.Cryptography;

namespace Kryolite.Shared.Blockchain;

[MessagePackObject]
public class View : Transaction
{
    public View()
    {

    }

    public View(PublicKey publicKey, long height, ImmutableList<SHA256Hash> parents)
    {
        TransactionType = TransactionType.VIEW;
        Value = Constant.VALIDATOR_REWARD;
        Data = BitConverter.GetBytes(height);
        Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Height = height;
        PublicKey = publicKey;
        Parents = parents;
    }

    public View(TransactionDto tx, ImmutableList<SHA256Hash> parents)
    {
        TransactionType = TransactionType.VIEW;
        PublicKey = tx.PublicKey ?? throw new Exception("view requires public key");
        To = tx.To;
        Value = tx.Value;
        Data = tx.Data;
        Timestamp = tx.Timestamp;
        Height = BitConverter.ToInt64(tx.Data);
        Signature = tx.Signature ?? throw new Exception("view requires signature");
        Parents = parents.Distinct().ToImmutableList();
        TransactionId = CalculateHash();
    }

    public View(Transaction tx)
    {
        Id = tx.Id;
        TransactionId = tx.TransactionId;
        Height = tx.Height;
        TransactionType = tx.TransactionType;
        PublicKey = tx.PublicKey ?? throw new Exception("view requires public key");
        To = tx.To;
        Value = tx.Value;
        Data = tx.Data;
        Timestamp = tx.Timestamp;
        Signature = tx.Signature ?? throw new Exception("view requires signature");
        ExecutionResult = tx.ExecutionResult;
        Parents = tx.Parents;
        Effects = tx.Effects;
    }
}
