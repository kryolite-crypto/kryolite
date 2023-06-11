using Kryolite.Shared.Dto;
using MessagePack;
using NSec.Cryptography;
using System.Security.Cryptography;

namespace Kryolite.Shared.Blockchain;

[MessagePackObject]
public class View : Transaction
{
    public View()
    {

    }

    public View(PublicKey publicKey, long height, List<SHA256Hash> parents)
    {
        TransactionType = TransactionType.VIEW;
        Value = Constant.VALIDATOR_REWARD;
        Data = BitConverter.GetBytes(height);
        Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Height = height;
        PublicKey = publicKey;
        Parents = parents;
    }

    public View(TransactionDto tx, List<SHA256Hash> parents)
    {
        TransactionType = TransactionType.VIEW;
        PublicKey = tx.PublicKey ?? throw new Exception("view requires public key");
        To = tx.To;
        Value = tx.Value;
        Pow = tx.Pow;
        Data = tx.Data;
        Timestamp = tx.Timestamp;
        Height = BitConverter.ToInt64(tx.Data);
        Signature = tx.Signature ?? throw new Exception("view requires signature");
        Parents = parents;
        TransactionId = CalculateHash();
    }

    public View(Transaction tx)
    {
        TransactionId = tx.TransactionId;
        TransactionType = TransactionType.VIEW;
        PublicKey = tx.PublicKey ?? throw new Exception("view requires public key");
        Height = tx.Height;
        To = tx.To;
        Value = tx.Value;
        Pow = tx.Pow;
        Data = tx.Data;
        Timestamp = tx.Timestamp;
        Height = tx.Height;
        Parents = tx.Parents;
        Signature = tx.Signature ?? throw new Exception("view requires signature");
    }
}
