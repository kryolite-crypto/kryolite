using Kryolite.Node.Storage;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using MessagePack;
using Microsoft.Data.Sqlite;
using NSec.Cryptography;
using RocksDbSharp;
using System;
using System.Buffers;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;

namespace Kryolite.Node.Repository;

public class StoreRepository : IStoreRepository, IDisposable
{
    private IStorage Storage { get; set; }

    public StoreRepository(IStorage storage)
    {
        Storage = storage ?? throw new ArgumentNullException(nameof(storage));
    }

    public void Add(Transaction tx)
    {
        var newEntity = tx.Id == 0;

        var keyBuf = ArrayPool<byte>.Shared.Rent(34);
        var keyMem = keyBuf.AsSpan();

        if (newEntity)
        {
            tx.Id = Storage.NextKey();
        }

        var id = BitConverter.GetBytes(tx.Id);

        // Transaction
        Storage.Put("Transaction", id, MessagePackSerializer.Serialize(tx));

        // TransactionId index
        Storage.Put("ixTransactionId", tx.TransactionId.Buffer, id);

        // Address index
        var addrKey = keyMem.Slice(0, 34);
        id.CopyTo(addrKey.Slice(26));

        if (tx.PublicKey is not null)
        {
            tx.From.Buffer.CopyTo(addrKey);
            Storage.Put("ixTransactionAddress", addrKey, id);
        }

        if (tx.To is not null)
        {
            tx.To.Buffer.CopyTo(addrKey);
            Storage.Put("ixTransactionAddress", addrKey, id);
        }

        // Height, TransactionType index
        var height = BitConverter.GetBytes(((ulong?)tx.Height) ?? ulong.MaxValue);
        Array.Reverse(height);

        var heightKey = keyMem.Slice(0, 17);

        height.CopyTo(heightKey);
        heightKey[8] = (byte)tx.TransactionType;
        id.CopyTo(heightKey.Slice(9));

        Storage.Put("ixTransactionHeight", heightKey, id);

        // Childless index
        Storage.Put("ixChildless", tx.TransactionId.Buffer, id);

        foreach (var parent in tx.Parents)
        {
            Storage.Delete("ixChildless", parent.Buffer);
        }

        ArrayPool<byte>.Shared.Return(keyBuf);
    }

    public void Finalize(List<Transaction> transactions)
    {
        if (transactions.Count == 0)
        {
            return;
        }

        var nullHeight = BitConverter.GetBytes(ulong.MaxValue);
        var key = ArrayPool<byte>.Shared.Rent(17);

        for (var i = 0; i < transactions.Count; i++)
        {
            key[8] = (byte)transactions[i].TransactionType;

            var id = BitConverter.GetBytes(transactions[i].Id);
            id.CopyTo(key, 9);

            var height = BitConverter.GetBytes(((ulong?)transactions[i].Height) ?? ulong.MaxValue);
            Array.Reverse(height);

            nullHeight.CopyTo(key, 0);
            Storage.Delete("ixTransactionHeight", key);

            height.CopyTo(key, 0);
            Storage.Put("ixTransactionHeight", key, id);
        }

        ArrayPool<byte>.Shared.Return(key);
    }

    public bool Exists(SHA256Hash transactionId)
    {
        return Storage.Exists("ixTransactionId", transactionId.Buffer);
    }

    public Transaction? Get(SHA256Hash transactionId)
    {
        var key = Storage.Get("ixTransactionId", transactionId.Buffer);

        if (key is null)
        {
            return null;
        }

        return Storage.Get<Transaction>("Transaction", key);
    }

    public List<Transaction> GetPending()
    {
        var height = BitConverter.GetBytes(uint.MaxValue);
        var ids = Storage.FindAll("ixTransactionHeight", height);

        return Storage.GetMany<Transaction>("Transaction", ids.ToArray());
    }

    public Genesis? GetGenesis()
    {
        var prefix = new byte[9];
        prefix[8] = (byte)TransactionType.GENESIS;

        var key = Storage.FindFirst("ixTransactionHeight", prefix);

        if (key is null)
        {
            return null;
        }

        var tx = Storage.Get<Transaction>("Transaction", key);

        if (tx is null)
        {
            return null;
        }

        return new Genesis(tx);
    }

    public View? GetLastView()
    {
        var chainKey = new byte[1];
        var chainState = Storage.Get<ChainState>("ChainState", chainKey);

        var heightBytes = BitConverter.GetBytes(chainState?.Height ?? 0);
        Array.Reverse(heightBytes);

        var prefix = new byte[9];

        heightBytes.CopyTo(prefix, 0);
        prefix[8] = (byte)TransactionType.VIEW;

        var key = Storage.FindFirst("ixTransactionHeight", prefix);

        if (key is null)
        {
            return null;
        }

        var tx = Storage.Get<Transaction>("Transaction", key);

        if (tx is null)
        {
            return null;
        }

        return new View(tx);
    }

    public List<Vote> GetVotesAtHeight(long height)
    {
        var heightBytes = BitConverter.GetBytes(height);
        Array.Reverse(heightBytes);

        var prefix = new byte[9];

        heightBytes.CopyTo(prefix, 0);
        prefix[8] = (byte)TransactionType.VOTE;

        var voteIds = Storage.FindAll("ixTransactionHeight", prefix);

        if (voteIds.Count == 0)
        {
            return new();
        }

        var votes = Storage.GetMany<Transaction>("Transaction", voteIds.ToArray());

        return votes.Where(x => x != null)
            .Select(x => new Vote(x))
            .ToList();
    }

    public ChainState? GetChainState()
    {
        var chainKey = new byte[1];
        return Storage.Get<ChainState>("ChainState", chainKey);
    }

    public void SaveState(ChainState chainState)
    {
        var chainKey = new byte[1];
        Storage.Put<ChainState>("ChainState", chainKey, chainState);
    }

    /*public void Delete(Transaction tx)
    {
        Context.Transactions.Remove(tx);
        Context.SaveChanges();
    }*/

    /*public void DeleteContractSnapshot(long height)
    {
        var snapshots = Context.ContractSnapshots.Where(x => x.Height > height);

        Context.ContractSnapshots.RemoveRange(snapshots);
        Context.SaveChanges();
    }*/

    public List<Transaction> GetLastNTransctions(Address address, int count)
    {
        /*using var cmd = Connection!.CreateCommand();

        cmd.CommandText = @"
            SELECT
                TransactionId,
                TransactionType,
                Height,
                PublicKey,
                Sender,
                Recipient,
                Value,
                Pow,
                Data,
                Timestamp,
                Signature,
                ExecutionResult
            FROM
                Transactions
            WHERE
                Sender = @addr OR Recipient = @addr
            ORDER BY DESC Height, Timestamp
            LIMIT @count
        ";

        cmd.Parameters.Add(new SQLiteParameter("@addr", address.ToString()));
        cmd.Parameters.Add(new SQLiteParameter("@count", count));

        using var reader = cmd.ExecuteReader(CommandBehavior.SequentialAccess);

        var results = new List<Transaction>();

        while (reader.Read())
        {
            results.Add(Transaction.Read(reader));
        }

        return results;*/

        return new();
    }

    public Ledger? GetWallet(Address address)
    {
        return Storage.Get<Ledger>("Ledger", address.Buffer);
    }

    public void UpdateWallet(Ledger ledger)
    {
        Storage.Put<Ledger>("Ledger", ledger.Address.Buffer, ledger);
    }

    public void UpdateWallets(IEnumerable<Ledger> ledgers)
    {
        foreach (var ledger in ledgers)
        {
            UpdateWallet(ledger);
        }
    }

    public void UpdateWallets(params Ledger[] ledgers)
    {
        foreach (var ledger in ledgers)
        {
            UpdateWallet(ledger);
        }
    }

    public Contract? GetContract(Address address)
    {
        /*using var cmd = Connection!.CreateCommand();

        cmd.CommandText = @"
            SELECT
                Address,
                Owner,
                Name,
                Balance,
                EntryPoint
            FROM
                Contract
            WHERE
                Address = @addr
        ";

        cmd.Parameters.Add(new SQLiteParameter("@addr", address.ToString()));

        using var reader = cmd.ExecuteReader();

        if (!reader.Read())
        {
            return null;
        }

        return Contract.Read(reader);*/
        return null;
    }

    public List<Ledger> GetRichList(int count)
    {
        /*using var cmd = Connection!.CreateCommand();

        cmd.CommandText = @"
            SELECT
                Address,
                Balance,
                Pending
            FROM
                Ledger
            ORDER BY Balance DESC
            LIMIT @count
        ";

        cmd.Parameters.Add(new SQLiteParameter("@count", count));

        using var reader = cmd.ExecuteReader();

        var results = new List<Ledger>();

        while (reader.Read())
        {
            results.Add(Ledger.Read(reader));
        }

        return results;*/
        return new();
    }

    public void AddContract(Contract contract)
    {
        /*using var cmd = Connection!.CreateCommand();

        cmd.CommandText = @"
            INSERT INTO Contract ( 
                Address,
                Owner,
                Name,
                Balance,
                EntryPoint
            ) VALUES (@addr, @owner, @name, @balance, @entry);
        ";

        cmd.Parameters.Add(new SQLiteParameter("@addr", contract.Address.ToString()));
        cmd.Parameters.Add(new SQLiteParameter("@owner", contract.Owner.ToString()));
        cmd.Parameters.Add(new SQLiteParameter("@name", contract.Name));
        cmd.Parameters.Add(new SQLiteParameter("@balance", contract.Balance));
        cmd.Parameters.Add(new SQLiteParameter("@entry", contract.EntryPoint));

        cmd.ExecuteNonQuery();*/
    }

    public void UpdateContracts(IEnumerable<Contract> contracts)
    {
        /*foreach (var contract in contracts)
        {
            AddContract(contract);
        }*/
    }

    public void UpdateToken(Token token)
    {
        /*using var cmd = Connection!.CreateCommand();

        cmd.CommandText = @"
            INSERT OR REPLACE INTO Token ( 
                TokenId,
                IsConsumed,
                Ledger,
                Contract
            ) VALUES (@id, @isconsumed, @ledger, @contract);
        ";

        cmd.Parameters.Add(new SQLiteParameter("@tokenid", token.TokenId.ToString()));
        cmd.Parameters.Add(new SQLiteParameter("@isconsumed", token.IsConsumed));
        cmd.Parameters.Add(new SQLiteParameter("@ledger", token.Ledger.ToString()));
        cmd.Parameters.Add(new SQLiteParameter("@contract", token.Contract.ToString()));

        cmd.ExecuteNonQuery();*/
    }

    public void UpdateTokens(IEnumerable<Token> tokens)
    {
        /*foreach (var token in tokens)
        {
            UpdateToken(token);
        }*/
    }

    public List<Transaction> GetTransactions(Address address)
    {
        /*using var cmd = Connection!.CreateCommand();

        cmd.CommandText = @"
            SELECT
                TransactionId,
                TransactionType,
                Height,
                PublicKey,
                Sender,
                Recipient,
                Value,
                Pow,
                Data,
                Timestamp,
                Signature,
                ExecutionResult
            FROM
                Transactions
            WHERE
                Sender = @addr
                OR
                Recipient = @addr
        ";

        cmd.Parameters.Add(new SQLiteParameter("@addr", address.ToString()));

        using var reader = cmd.ExecuteReader();

        var results = new List<Transaction>();

        while (reader.Read())
        {
            results.Add(Transaction.Read(reader));
        }

        return results;*/
        return new();
    }

    public List<SHA256Hash> GetTransactionsToValidate()
    {
        var hashes = Storage.FindAll("ixChildless")
            .Select(x => (SHA256Hash)x)
            .ToList(); ;

        if (hashes.Count < 2)
        {
            var ids = Storage.FindLast("ixTransactionHeight", 2 + hashes.Count);

            foreach (var id in ids)
            {
                var tx = Storage.Get<Transaction>("Transaction", id);

                if (tx is null)
                {
                    continue;
                }

                hashes.Add(tx.TransactionId);

                if (hashes.Count >= 2)
                {
                    break;
                }
            }
        }

        return hashes;
    }

    public Token? GetToken(SHA256Hash tokenId)
    {
        /*using var cmd = Connection!.CreateCommand();

        cmd.CommandText = @"
            SELECT
                TokenId,
                IsConsumed,
                Ledger,
                Contract
            FROM
                Token
            WHERE
                TokenId = @tokenid
        ";

        cmd.Parameters.Add(new SQLiteParameter("@tokenid", tokenId));

        using var reader = cmd.ExecuteReader();

        var results = new List<Token>();

        if (!reader.Read())
        {
            return null;
        }

        return Token.Read(reader);*/
        return null;
    }

    public Token? GetToken(Address ledger, SHA256Hash tokenId)
    {
        /*using var cmd = Connection!.CreateCommand();

        cmd.CommandText = @"
            SELECT
                TokenId,
                IsConsumed,
                Ledger,
                Contract
            FROM
                Token
            WHERE
                TokenId = @tokenid && Ledger = @ledger
        ";

        cmd.Parameters.Add(new SQLiteParameter("@tokenid", tokenId.ToString()));
        cmd.Parameters.Add(new SQLiteParameter("@ledger", ledger.ToString()));

        using var reader = cmd.ExecuteReader();

        var results = new List<Token>();

        if (!reader.Read())
        {
            return null;
        }

        return Token.Read(reader);*/
        return null;
    }

    public List<Token> GetTokens(Address ledger)
    {
        /*using var cmd = Connection!.CreateCommand();

        cmd.CommandText = @"
            SELECT
                TokenId,
                IsConsumed,
                Ledger,
                Contract
            FROM
                Token
            WHERE
                Ledger = @ledger
        ";

        cmd.Parameters.Add(new SQLiteParameter("@ledger", ledger.ToString()));

        using var reader = cmd.ExecuteReader();

        var results = new List<Token>();

        while (reader.Read())
        {
            results.Add(Token.Read(reader));
        }

        return results;*/
        return new();
    }

    public List<Token> GetContractTokens(Address contractAddress)
    {
        /*using var cmd = Connection!.CreateCommand();

        cmd.CommandText = @"
            SELECT
                TokenId,
                IsConsumed,
                Address
            FROM
                Token
            WHERE
                ContractAddress = @contract
        ";

        cmd.Parameters.Add(new SQLiteParameter("@contract", contractAddress));

        using var reader = cmd.ExecuteReader();

        var results = new List<Token>();

        while (reader.Read())
        {
            results.Add(Token.Read(reader));
        }

        return results;*/
        return new();
    }

    public void Dispose()
    {

    }

    public long? GetTimestamp(SHA256Hash transactionId)
    {
        return Get(transactionId)?.Timestamp;
        // throw new NotImplementedException();
    }
}
