using MessagePack;
using NSec.Cryptography;
using System.Security.Cryptography;

namespace Kryolite.Shared.Blockchain;

[MessagePackObject]
public class Heartbeat : Transaction
{
    [IgnoreMember]
    public long Height { get; set; }
    [IgnoreMember]
    public List<HeartbeatSignature> Signatures { get; set; } = new List<HeartbeatSignature>();

    public static Heartbeat Create(PublicKey publicKey, PrivateKey privateKey, long height)
    {
        var heartbeat = new Heartbeat
        {
            TransactionType = TransactionType.HEARTBEAT,
            Value = Constant.VALIDATOR_REWARD,
            Data = BitConverter.GetBytes(height),
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Height = height
        };

        var signature = new HeartbeatSignature
        {
            TransactionId = heartbeat.TransactionId,
            Height = height,
            PublicKey = publicKey
        };

        signature.Sign(privateKey);

        heartbeat.Signatures.Add(signature);

        return heartbeat;
    }

    public override SHA256Hash CalculateHash()
    {
        using var sha256 = SHA256.Create();
        using var stream = new MemoryStream();

        stream.Write(BitConverter.GetBytes(Value));
        stream.Write(Data);

        foreach (var tx in Validates.OrderBy(x => x.TransactionId))
        {
            stream.Write(tx.TransactionId);
        }

        stream.Flush();
        stream.Position = 0;

        return sha256.ComputeHash(stream);
    }
}
