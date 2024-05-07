using Kryolite.ByteSerializer;
using Kryolite.Node.Storage;
using Kryolite.RocksDb;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using Microsoft.Extensions.Configuration;
using System.Buffers;
using System.Data;
using System.Runtime.InteropServices;

namespace Kryolite.Node.Repository;

public class StoreRepository : IStoreRepository, IDisposable
{
    private IStorage Storage { get; set; }
    private IConfiguration? Configuration { get; }

    public StoreRepository(IStorage storage, IConfiguration configuration)
    {
        Storage = storage ?? throw new ArgumentNullException(nameof(storage));
        Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    public StoreRepository(string storePath)
    {
        var storage = new RocksDBStorage(storePath);
        Storage = storage ?? throw new ArgumentNullException(nameof(storage));
    }

    public bool BlockExists(SHA256Hash blockhash)
    {
        return Storage.Exists("Block", blockhash.Buffer);
    }

    public bool VoteExists(SHA256Hash votehash)
    {
        return Storage.Exists("Vote", votehash.Buffer);
    }

    public bool TransactionExists(SHA256Hash transactionId)
    {
        return Storage.Exists("Transaction", transactionId.Buffer);
    }

    public View? GetView(long height)
    {
        return Storage.Get<View>("View", height.ToKey());
    }

    public View? GetView(SHA256Hash viewHash)
    {
        var id = Storage.Get("ixViewHash", (byte[])viewHash);

        if (id is null)
        {
            return null;
        }

        return Storage.Get<View>("View", id);
    }

    public Block? GetBlock(SHA256Hash blockhash)
    {
        return Storage.Get<Block>("Block", blockhash.Buffer);
    }

    public Vote? GetVote(SHA256Hash votehash)
    {
        return Storage.Get<Vote>("Vote", votehash.Buffer);
    }

    public Transaction? GetTransaction(SHA256Hash transactionId)
    {
        var key = Storage.Get("ixTransactionId", transactionId.Buffer);

        if (key is null)
        {
            return null;
        }

        return Storage.Get<Transaction>("Transaction", key);
    }

    public void Add(Block block)
    {
        Storage.Put("Block", block.GetHash(), Serializer.Serialize<Block>(block), CurrentTransaction);
    }

    public void Add(View view)
    {
        var key = view.Id.ToKey();
        Storage.Put("View", key, Serializer.Serialize<View>(view), CurrentTransaction);
        Storage.Put("ixViewHash", view.GetHash(), key, CurrentTransaction);
    }

    public void Add(Vote vote)
    {
        Storage.Put("Vote", vote.GetHash(), Serializer.Serialize<Vote>(vote), CurrentTransaction);
    }

    public void Add(Transaction tx)
    {
        Span<byte> keyBuf = stackalloc byte[34];
        var transactionId = tx.CalculateHash();

        if (tx.Id == 0)
        {
            tx.Id = (long)Storage.NextKey();
        }

        var id = tx.Id.ToKey();

        // Transaction
        Storage.Put("Transaction", id, Serializer.Serialize<Transaction>(tx), CurrentTransaction);

        //ixTransactionId index
        Storage.Put("ixTransactionId", transactionId.Buffer, id, CurrentTransaction);

        // Address index
        var addrKey = keyBuf[..34];
        id.CopyTo(addrKey[Address.ADDRESS_SZ..]);

        if (tx.PublicKey is not null)
        {
            tx.From!.Buffer.CopyTo(addrKey);
            Storage.Put("ixTransactionAddress", addrKey, id, CurrentTransaction);
        }

        if (tx.To is not null)
        {
            tx.To.Buffer.CopyTo(addrKey);
            Storage.Put("ixTransactionAddress", addrKey, id, CurrentTransaction);
        }

        if (tx.ExecutionResult == ExecutionResult.SCHEDULED)
        {
            AddDueTransaction(tx);
        }
    }

    public void AddRange(List<Block> blocks)
    {
        foreach (var block in blocks)
        {
            Add(block);
        }
    }

    public void AddRange(List<Vote> votes)
    {
        foreach (var vote in votes)
        {
            Add(vote);
        }
    }

    public void AddRange(List<Transaction> transactions)
    {
        foreach (var tx in transactions)
        {
            Add(tx);
        }
    }

    public List<Block> GetBlocks(List<SHA256Hash> blockhashes)
    {
        return Storage.GetMany<Block>("Block", blockhashes.Select(x => x.Buffer).ToArray());
    }

    public List<Vote> GetVotes(List<SHA256Hash> votehashes)
    {
        return Storage.GetMany<Vote>("Vote", votehashes.Select(x => x.Buffer).ToArray());
    }

    public List<Transaction> GetTransactions(List<SHA256Hash> transactionIds)
    {
        if (transactionIds.Count == 0)
        {
            return new();
        }

        var keys = Storage.GetMany("ixTransactionId", transactionIds.Select(x => x.Buffer).ToArray());
        return Storage.GetMany<Transaction>("Transaction", keys);
    }

    public View? GetViewAt(long height)
    {
        var heightBytes = BitConverter.GetBytes(height);
        Array.Reverse(heightBytes);

        return Storage.Get<View>("View", heightBytes);
    }

    public View? GetLastView()
    {
        var chainState = Storage.FindLast<ChainState>("ChainState");

        var heightBytes = chainState?.Id ?? 0;
        return Storage.Get<View>("View", heightBytes.ToKey());
    }

    public long GetLastHeightContainingBlock()
    {
        using var opts = new ReadOptions();
        using var iterator = Storage.GetIterator("View", opts);

        iterator.SeekToLast();

        while (iterator.Valid())
        {
            var view = Serializer.Deserialize<View>(iterator.Value());

            if (view?.Blocks.Count > 0)
            {
                return view.Id;
            }

            iterator.Prev();
        }

        // View #1 is the first possible view with blocks
        return 1;
    }

    public List<Transaction> GetTransactionsAtHeight(long height)
    {
        var view = Storage.Get<View>("View", height.ToKey());

        if (view is null)
        {
            return new();
        }

        var transactions = new List<Transaction>(view.Transactions.Count + view.Rewards.Count);

        if (view.Transactions.Count > 0)
        {
            var keys = Storage.GetMany("ixTransactionId", view.Transactions.Select(x => x.Buffer).ToArray());
            transactions.AddRange(Storage.GetMany<Transaction>("Transaction", keys));
        }

        if (view.Rewards.Count > 0)
        {
            var keys = Storage.GetMany("ixTransactionId", view.Rewards.Select(x => x.Buffer).ToArray());
            transactions.AddRange(Storage.GetMany<Transaction>("Transaction", keys));
        }

        return transactions;
    }

    public List<Vote> GetVotesAtHeight(long height)
    {
        var view = Storage.Get<View>("View", height.ToKey());

        if (view is null || view.Votes.Count == 0)
        {
            return new();
        }

        var keys = view.Votes.Select(x => x.Buffer).ToArray();
        return Storage.GetMany<Vote>("Vote", keys);
    }

    public ChainState? GetChainState()
    {
        return Storage.FindLast<ChainState>("ChainState");
    }

    public ChainState? GetChainState(long height)
    {
        return Storage.Get<ChainState>("ChainState", height.ToKey());
    }

    public void SaveState(ChainState chainState)
    {
        Storage.Put("ChainState", chainState.Id.ToKey(), chainState, CurrentTransaction);
    }

    public List<Transaction> GetLastNTransctions(int count)
    {
        return Storage.FindLast<Transaction>("Transaction", count);
    }

    public List<Transaction> GetLastNTransctions(Address address, int count)
    {
        var ids = Storage.FindLast("ixTransactionAddress", address, 5);

        if (ids is null || ids.Count == 0)
        {
            return new();
        }

        return Storage.GetMany<Transaction>("Transaction", ids.ToArray());
    }

    public Ledger? GetWallet(Address address)
    {
        return Storage.Get<Ledger>("Ledger", address.Buffer);
    }

    public void UpdateWallet(Ledger ledger)
    {
        Storage.Put<Ledger>("Ledger", ledger.Address.Buffer, ledger, CurrentTransaction);
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
        return Storage.Get<Contract>("Contract", (byte[])address);
    }

    public byte[]? GetContractCode(Address address)
    {
        return Storage.Get("ContractCode", (byte[])address);
    }

    public byte[]? GetLatestSnapshot(Address address)
    {
        return Storage.FindLast("ContractSnapshot", address);
    }

    public List<Ledger> GetRichList(int count)
    {
        var ledger = Storage.GetAll<Ledger>("Ledger");

        return ledger.OrderByDescending(x => x.Balance)
            .Take(count)
            .ToList();
    }

    public void AddContract(Contract contract)
    {
        Storage.Put<Contract>("Contract", contract.Address.Buffer, contract, CurrentTransaction);
    }

    public void AddContractCode(Address contract, byte[] code)
    {
        Storage.Put("ContractCode", contract.Buffer, code, CurrentTransaction);
    }

    public void AddContractSnapshot(Address contract, long height, byte[] snapshot)
    {
        Span<byte> keyBuf = stackalloc byte[Address.ADDRESS_SZ + sizeof(long)];
        contract.Buffer.CopyTo(keyBuf);
        height.ToKey().CopyTo(keyBuf.Slice(Address.ADDRESS_SZ));

        Storage.Put("ContractSnapshot", keyBuf, snapshot, CurrentTransaction);
    }

    public void UpdateContract(Contract contract)
    {
        AddContract(contract);
    }

    public void UpdateContracts(IEnumerable<Contract> contracts)
    {
        foreach (var contract in contracts)
        {
            AddContract(contract);
        }
    }

    public void AddToken(Token token)
    {
        Span<byte> keyBuf = stackalloc byte[58];

        if (token.Id == 0)
        {
            token.Id = Storage.NextKey(CurrentTransaction);
        }

        var id = token.Id.ToKey();
        Storage.Put("Token", id, Serializer.Serialize(token), CurrentTransaction);

        // ContractAddress_TokenId
        var tokenIx = keyBuf[..58];
        token.Contract.Buffer.CopyTo(tokenIx);
        token.TokenId.Buffer.CopyTo(tokenIx[Address.ADDRESS_SZ..]);

        Storage.Put("ixTokenId", tokenIx, id, CurrentTransaction);

        // LedgerAddress_Key
        var ledgerIx = keyBuf[..34];
        token.Ledger.Buffer.CopyTo(ledgerIx);
        id.CopyTo(ledgerIx[Address.ADDRESS_SZ..]);

        Storage.Put("ixTokenLedger", ledgerIx, id, CurrentTransaction);
    }

    public void UpdateToken(Token token)
    {
        if (token.Id != 0)
        {
            Span<byte> keyBuf = stackalloc byte[34];
            var id = BitConverter.GetBytes(token.Id);

            var oldToken = Storage.Get<Token>("Token", id);

            if (oldToken is not null)
            {
                // LedgerAddress_Key
                token.Ledger.Buffer.CopyTo(keyBuf);
                id.CopyTo(keyBuf[Address.ADDRESS_SZ..]);

                Storage.Delete("ixTokenLedger", keyBuf, CurrentTransaction);
            }
        }

        AddToken(token);
    }

    public void UpdateTokens(IEnumerable<Token> tokens)
    {
        foreach (var token in tokens)
        {
            UpdateToken(token);
        }
    }

    public void DeleteToken(Token token)
    {
        Span<byte> keyBuf = stackalloc byte[84];

        var id = BitConverter.GetBytes(token.Id);
        Storage.Delete("Token", id, CurrentTransaction);

        // ContractAddress_TokenId
        var tokenIx = keyBuf.Slice(0, 58);
        token.Contract.Buffer.AsSpan().CopyTo(tokenIx);
        token.TokenId.Buffer.AsSpan().CopyTo(tokenIx.Slice(Address.ADDRESS_SZ));

        Storage.Delete("ixTokenId", tokenIx, CurrentTransaction);

        // LedgerAddress_Key
        var ledgerIx = keyBuf.Slice(0, 34);
        token.Ledger.Buffer.AsSpan().CopyTo(ledgerIx);
        id.CopyTo(ledgerIx.Slice(Address.ADDRESS_SZ));

        Storage.Delete("ixTokenLedger", ledgerIx, CurrentTransaction);
    }

    public List<Transaction> GetTransactions(Address address)
    {
        var ids = Storage.FindLast("ixTransactionAddress", address, -1);
        return Storage.GetMany<Transaction>("Transaction", ids.ToArray());
    }

    public Token? GetToken(Address contract, SHA256Hash tokenId)
    {
        var id = new byte[58];

        // ContractAddress_TokenId
        contract.Buffer.CopyTo(id, 0);
        tokenId.Buffer.CopyTo(id, Address.ADDRESS_SZ);

        var key = Storage.Get("ixTokenId", id);

        if (key is null)
        {
            return null;
        }

        return Storage.Get<Token>("Token", key);
    }

    public List<Token> GetTokens(Address ledger)
    {
        var keys = Storage.FindAll("ixTokenLedger", ledger.Buffer);
        return Storage.GetMany<Token>("Token", [.. keys]);
    }

    public List<Token> GetContractTokens(Address contractAddress)
    {
        var keys = Storage.FindAll("ixTokenId", contractAddress.Buffer);
        return Storage.GetMany<Token>("Token", [.. keys]);
    }

    public long? GetTimestamp(SHA256Hash transactionId)
    {
        return GetTransaction(transactionId)?.Timestamp;
    }

    public bool IsValidator(Address address)
    {
        if (Constant.SEED_VALIDATORS.Contains(address))
        {
            return true;
        }

        var stake = Storage.Get<Validator>("Validator", address.Buffer);

        if (stake == null)
        {
            return false;
        }

        return stake.Stake >= Constant.MIN_STAKE;
    }

    public Validator? GetStake(Address address)
    {
        return Storage.Get<Validator>("Validator", address.Buffer);
    }

    public void SetStake(Address address, Validator stake)
    {
        Storage.Put("Validator", address.Buffer, stake, CurrentTransaction);
    }

    public void DeleteStake(Address address)
    {
        Storage.Delete("Validator", address.Buffer, CurrentTransaction);
    }

    public List<Validator> GetValidators()
    {
        var validators = new Dictionary<Address, Validator>();

        using var opts = new ReadOptions();
        using var iterator = Storage.GetIterator("Validator", opts);

        iterator.SeekToLast();

        while (iterator.Valid())
        {
            var addr = (Address)iterator.Key().AsSpan().Slice(0, Address.ADDRESS_SZ);

            if (!validators.ContainsKey(addr))
            {
                var validator = Serializer.Deserialize<Validator>(iterator.Value()) ?? throw new Exception("failed to deserialize validator");
                validators.Add(addr, validator);
            }

            iterator.Prev();
        }

        return validators.Values
            .OrderByDescending(x => x.Stake)
            .ToList();
    }

    public void AddDueTransaction(Transaction tx)
    {
        var id = tx.Id.ToKey();
        var ts = tx.Timestamp.ToKey();

        Span<byte> tsKey = stackalloc byte[(sizeof(long) * 2)]; // timestamp + id

        ts.CopyTo(tsKey);
        id.CopyTo(tsKey[sizeof(long)..]);

        Storage.Put("ixScheduledTransaction", tsKey, id, CurrentTransaction);
    }

    public List<Transaction> GetDueTransactions(long timestamp, bool delete)
    {
        var ts = timestamp.ToKey();

        Span<byte> key = stackalloc byte[sizeof(long) * 2]; // timestamp + id
        key.Fill(255);
        ts.CopyTo(key);

        var lowerBound = new byte[sizeof(long) * 2];
        ts.CopyTo(lowerBound, 0);

        using var opts = new ReadOptions();
        opts.IterateLowerBound(lowerBound);

        using var iterator = Storage.GetIterator("ixScheduledTransaction", opts);

        iterator.SeekForPrev(key);

        var results = new List<Transaction>();
        var toRemove = new List<byte>();

        while (iterator.Valid())
        {
            var id = iterator.Value();
            var tx = Storage.Get<Transaction>("Transaction", id);

            if (delete)
            {
                Storage.Delete("ixScheduledTransaction", iterator.Key(), CurrentTransaction);
            }

            iterator.Prev();

            if (tx is not null)
            {
                results.Add(tx);
            }
        }

        return results;
    }

    public List<Transaction> GetTransactions(int pageNum, int pageSize)
    {
        return Storage.GetRange<Transaction>("Transaction", pageNum, pageSize);
    }

    private ITransaction? _currentTransaction;

    private ITransaction? CurrentTransaction
    {
        get => _currentTransaction is null || _currentTransaction.IsDisposed ?
            null : _currentTransaction;
        set => _currentTransaction = value;
    }

    public ITransaction BeginTransaction()
    {
        if (CurrentTransaction is null || CurrentTransaction.IsDisposed)
        {
            CurrentTransaction = Storage.BeginTransaction();
        }

        return CurrentTransaction;
    }

    public void Reset()
    {
        Storage.Reset();
    }

    public void ReplaceDbFrom(string storeName)
    {
        var dataDir = Configuration?.GetValue<string>("data-dir") ?? Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".kryolite");

        var activeStore = Path.Combine(dataDir, $"store");
        var stagingStore = Path.Combine(dataDir, $"store.{storeName}");

        Storage.Close();

        if (Directory.Exists(activeStore))
        {
            Directory.Delete(activeStore, true);
        }

        Directory.Move(stagingStore, activeStore);

        Storage.Open(activeStore);
    }

    public List<Transaction> GetVotesForAddress(Address address, int count)
    {
        Span<byte> key = stackalloc byte[Address.ADDRESS_SZ + sizeof(long)];
        key.Fill(255);
        address.Buffer.CopyTo(key);

        var lowerBound = new byte[Address.ADDRESS_SZ + sizeof(long)];
        address.Buffer.CopyTo(lowerBound, 0);

        using var opts = new ReadOptions();
        opts.IterateLowerBound(lowerBound);

        using var iterator = Storage.GetIterator("ixTransactionAddress", opts);

        iterator.SeekForPrev(key);

        var results = new List<Transaction>(count);

        while (iterator.Valid())
        {
            var id = iterator.Value();
            var tx = Storage.Get<Transaction>("Transaction", id);

            iterator.Prev();

            if (tx is null)
            {
                continue;
            }

            if (tx.TransactionType != TransactionType.STAKE_REWARD)
            {
                continue;
            }

            results.Add(tx);

            if (results.Count >= count)
            {
                break;
            }
        }

        return results;
    }

    public void Close()
    {
        Storage.Close();
    }

    public void Dispose()
    {

    }

    public Checkpoint CreateCheckpoint()
    {
        return Storage.CreateCheckpoint();
    }

    public void DeleteBlock(SHA256Hash blockhash)
    {
        Storage.Delete("Block", blockhash, CurrentTransaction);
    }

    public void DeleteVote(SHA256Hash votehash)
    {
        Storage.Delete("Vote", votehash, CurrentTransaction);
    }

    public void DeleteBlocks(List<SHA256Hash> blockhashes)
    {
        foreach (var blockhash in blockhashes)
        {
            DeleteBlock(blockhash);
        }
    }

    public void DeleteVotes(List<SHA256Hash> votehashes)
    {
        foreach (var votehash in votehashes)
        {
            DeleteVote(votehash);
        }
    }

    public void Delete(View view)
    {
        var key = view.Id.ToKey();
        Storage.Delete("View", key, CurrentTransaction);
        Storage.Delete("ixViewHash", view.GetHash(), CurrentTransaction);
    }

    public void Delete(Transaction tx)
    {
        Span<byte> keyBuf = stackalloc byte[34];
        var transactionId = tx.CalculateHash();

        var id = tx.Id.ToKey();

        // Transaction
        Storage.Delete("Transaction", id, CurrentTransaction);

        // transactionId
        Storage.Delete("ixTransactionId", transactionId.Buffer, CurrentTransaction);

        // Address index
        var addrKey = keyBuf[..34];
        id.CopyTo(addrKey[Address.ADDRESS_SZ..]);

        if (tx.PublicKey is not null)
        {
            tx.From!.Buffer.CopyTo(addrKey);
            Storage.Delete("ixTransactionAddress", addrKey, CurrentTransaction);
        }

        if (tx.To is not null)
        {
            tx.To.Buffer.CopyTo(addrKey);
            Storage.Delete("ixTransactionAddress", addrKey, CurrentTransaction);
        }

        var ts = tx.Timestamp.ToKey();
        Span<byte> tsKey = stackalloc byte[(sizeof(long) * 2)]; // timestamp + id

        ts.CopyTo(tsKey);
        id.CopyTo(tsKey[sizeof(long)..]);

        Storage.Delete("ixScheduledTransaction", tsKey, CurrentTransaction);
    }

    public void DeleteContract(Address contract)
    {
        Storage.Delete("Contract", contract.Buffer, CurrentTransaction);
    }

    public void DeleteContractCode(Address contract)
    {
        Storage.Delete("ContractCode", contract.Buffer, CurrentTransaction);
    }

    public void DeleteContractSnapshot(Address contract, long height)
    {
        Span<byte> keyBuf = stackalloc byte[Address.ADDRESS_SZ + sizeof(long)];
        contract.Buffer.CopyTo(keyBuf);
        height.ToKey().CopyTo(keyBuf.Slice(Address.ADDRESS_SZ));

        Storage.Delete("ContractSnapshot", keyBuf, CurrentTransaction);
    }

    public void DeleteState(long height)
    {
        Storage.Delete("ChainState", height.ToKey(), CurrentTransaction);
    }

}
