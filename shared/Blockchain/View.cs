using Kryolite.Shared.Dto;
using MessagePack;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using System.Security.Cryptography;

namespace Kryolite.Shared.Blockchain;

[MessagePackObject]
public class View : Transaction
{
    [IgnoreMember]
    public List<Vote> Votes { get; set; } = new List<Vote>();

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
        TransactionType = tx.TransactionType;
        PublicKey = tx.PublicKey ?? throw new Exception("view requires public key");
        To = tx.To;
        Value = tx.Value;
        Pow = tx.Pow ?? new SHA256Hash();
        Data = tx.Data;
        Timestamp = tx.Timestamp;
        Signature = tx.Signature ?? throw new Exception("view requires signature");
        Parents = parents;
        TransactionId = CalculateHash();
    }

    public View(Transaction tx)
    {
        TransactionId = tx.TransactionId;
        TransactionType = tx.TransactionType;
        PublicKey = tx.PublicKey ?? throw new Exception("view requires public key");
        Height = tx.Height;
        To = tx.To;
        Value = tx.Value;
        Pow = tx.Pow ?? new SHA256Hash();
        Data = tx.Data;
        Timestamp = tx.Timestamp;
        Signature = tx.Signature ?? throw new Exception("view requires signature");
    }

    public Vote Vote(PrivateKey privateKey)
    {
        var vote = new Vote
        {
            TransactionId = CalculateHash(),
            PublicKey = PublicKey ?? throw new Exception("View requires PublicKey")
        };

        vote.Sign(privateKey);

        Votes.Add(vote);

        return vote;
    }

    public override SHA256Hash CalculateHash()
    {
        using var sha256 = SHA256.Create();
        using var stream = new MemoryStream();

        stream.Write(BitConverter.GetBytes(Value));
        stream.Write(Data);

        foreach (var hash in Parents.Order())
        {
            stream.Write(hash);
        }

        stream.Flush();
        stream.Position = 0;

        return sha256.ComputeHash(stream);
    }
}
