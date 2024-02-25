﻿using Kryolite.EventBus;
using Kryolite.Shared.Blockchain;
using MemoryPack;
using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using System.Security.Cryptography;

namespace Kryolite.Shared.Dto;

[DataContract]
[MemoryPackable]
public partial class TransactionDto : EventBase
{
    [DataMember]
    public TransactionType TransactionType { get; init; }

    [DataMember]
    public PublicKey PublicKey { get; init; } = PublicKey.NULL_PUBLIC_KEY;

    [DataMember]
    public Address To { get; init; } = Address.NULL_ADDRESS;

    [DataMember]
    public ulong Value { get; init; }

    [DataMember]
    public byte[]? Data { get; init; }

    [DataMember]
    public long Timestamp { get; init; }

    [DataMember]
    public Signature Signature { get; init; } = Signature.NULL_SIGNATURE;
    
    [MemoryPackIgnore]
    public bool IsValid { get; set; }

    private SHA256Hash? CachedTransactionId { get; set; }

    public SHA256Hash CalculateHash(bool forceRefresh = false)
    {
        if (CachedTransactionId is not null && !forceRefresh)
        {
            return CachedTransactionId;
        }

        using var sha256 = SHA256.Create();
        using var stream = new MemoryStream();

        stream.WriteByte((byte)TransactionType);

        if (PublicKey is not null)
        {
            stream.Write(PublicKey ?? throw new Exception("public key required when hashing payment"));
        }

        if (To is not null)
        {
            stream.Write(To);
        }

        stream.Write(BitConverter.GetBytes(Value));
        stream.Write(Data);
        stream.Write(BitConverter.GetBytes(Timestamp));
        stream.Flush();
        stream.Position = 0;

        CachedTransactionId = sha256.ComputeHash(stream.ToArray());

        return CachedTransactionId;
    }

    [MemoryPackConstructor]
    public TransactionDto()
    {

    }

    public TransactionDto(Transaction tx)
    {
        TransactionType = tx.TransactionType;
        PublicKey = tx.PublicKey;
        To = tx.To;
        Value = tx.Value;
        Data = tx.Data;
        Timestamp = tx.Timestamp;
        Signature = tx.Signature;
    }
}