using Kryolite.Shared.Dto;
using MessagePack;
using NSec.Cryptography;
using System.Security.Cryptography;

namespace Kryolite.Shared.Blockchain;

[MessagePackObject]
public class Vote : Transaction
{
    public Vote()
    {

    }

    public Vote(PublicKey publicKey, List<SHA256Hash> parents)
    {
        TransactionType = TransactionType.VOTE;
        PublicKey = publicKey;
        Value = 0;
        Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Parents = parents;

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
        TransactionId = tx.TransactionId;
        TransactionType = TransactionType.VOTE;
        PublicKey = tx.PublicKey ?? throw new Exception("vote requires public key");
        Height = tx.Height;
        To = tx.To;
        Value = tx.Value;
        Data = tx.Data;
        Timestamp = tx.Timestamp;
        Signature = tx.Signature ?? throw new Exception("vote requires signature");
    }
}
