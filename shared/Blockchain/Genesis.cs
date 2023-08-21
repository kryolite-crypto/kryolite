using MessagePack;
using NSec.Cryptography;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Kryolite.Shared.Blockchain;

[MessagePackObject]
public class Genesis : Transaction
{
    public Genesis()
    {

    }

    public Genesis(Transaction tx)
    {
        Id = tx.Id;
        TransactionId = tx.TransactionId;
        Height = tx.Height;
        TransactionType = tx.TransactionType;
        PublicKey = tx.PublicKey;
        To = tx.To;
        Value = tx.Value;
        Data = tx.Data;
        Timestamp = tx.Timestamp;
        Signature = tx.Signature;
        ExecutionResult = tx.ExecutionResult;
        Parents = tx.Parents;
        Effects = tx.Effects;
    }
}
