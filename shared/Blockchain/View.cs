using MessagePack;
using NSec.Cryptography;
using System.Security.Cryptography;

namespace Kryolite.Shared.Blockchain;

[MessagePackObject]
public class View : Transaction
{
    [IgnoreMember]
    public List<Vote> Votes { get; set; } = new List<Vote>();

    public static View Create(PublicKey publicKey, PrivateKey privateKey, long height)
    {
        var view = new View
        {
            TransactionType = TransactionType.VIEW,
            Value = Constant.VALIDATOR_REWARD,
            Data = BitConverter.GetBytes(height),
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Height = height
        };

        var signature = new Vote
        {
            TransactionId = view.TransactionId,
            Height = height,
            PublicKey = publicKey
        };

        signature.Sign(privateKey);

        view.Votes.Add(signature);

        return view;
    }

    public override SHA256Hash CalculateHash()
    {
        using var sha256 = SHA256.Create();
        using var stream = new MemoryStream();

        stream.Write(BitConverter.GetBytes(Value));
        stream.Write(Data);

        try
        {
            foreach (var tx in Validates.OrderBy(x => x.TransactionId).ToList())
            {
                stream.Write(tx.TransactionId);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
        }

        stream.Flush();
        stream.Position = 0;

        return sha256.ComputeHash(stream);
    }
}
