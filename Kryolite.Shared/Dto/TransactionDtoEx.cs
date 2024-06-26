using Kryolite.EventBus;
using Kryolite.Shared.Blockchain;
using Kryolite.Type;
using System.Security.Cryptography;

namespace Kryolite.Shared.Dto;

public partial class TransactionDtoEx
{

    public SHA256Hash? TransactionId { get; set; }
    public TransactionType TransactionType { get; set; }
    public PublicKey PublicKey { get; set; }
    public Address From { get; set; }
    public Address To { get; set; }
    public ulong Value { get; set; }
    public uint MaxFee { get; set; }
    public uint SpentFee { get; set; }
    public byte[]? Data { get; set; }
    public long Timestamp { get; set; }
    public Signature Signature { get; set; }
    public ExecutionResult ExecutionResult { get; set; }
    public List<Effect> Effects { get; set; }

    public TransactionDtoEx(Transaction tx)
    {
        TransactionId = tx.CalculateHash();
        TransactionType = tx.TransactionType;
        PublicKey = tx.PublicKey;
        From = tx.PublicKey.ToAddress();
        To = tx.To;
        Value = tx.Value;
        MaxFee = tx.MaxFee;
        SpentFee = tx.SpentFee;
        Data = tx.Data;
        Timestamp = tx.Timestamp;
        Signature = tx.Signature;
        ExecutionResult = tx.ExecutionResult;
        Effects = tx.Effects;
    }
}
