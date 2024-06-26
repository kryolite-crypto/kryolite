using Kryolite.ByteSerializer;
using Kryolite.Node.Storage;
using Kryolite.Node.Storage.Key;
using Kryolite.RocksDb;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using Kryolite.Type;
using Microsoft.Extensions.Configuration;
using System.Buffers;
using System.Data;

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
        Storage = storage ?? throw new ArgumentNullException(nameof(storePath));
    }

    public bool BlockExists(SHA256Hash blockhash)
    {
        var key = new BlockKey(blockhash);
        return Storage.Exists(BlockKey.KeyName, key);
    }

    public bool VoteExists(SHA256Hash votehash)
    {
        var key = new VoteKey(votehash);
        return Storage.Exists(VoteKey.KeyName, key);
    }

    public bool TransactionExists(SHA256Hash transactionId)
    {
        var key = new TransactionIdKey(transactionId);
        return Storage.Exists(TransactionIdKey.KeyName, key);
    }

    public View? GetView(long height)
    {
        var key = new ViewKey(height);
        return Storage.Get<View>(ViewKey.KeyName, key);
    }

    public View? GetView(SHA256Hash viewHash)
    {
        var hashKey = new ViewHashKey(viewHash);
        var id = Storage.Get(ViewHashKey.KeyName, hashKey);

        if (id is null)
        {
            return null;
        }

        return Storage.Get<View>(ViewKey.KeyName, id);
    }

    public Block? GetBlock(SHA256Hash blockhash)
    {
        var key = new BlockKey(blockhash);
        return Storage.Get<Block>(BlockKey.KeyName, key);
    }

    public Vote? GetVote(SHA256Hash votehash)
    {
        var key = new VoteKey(votehash);
        return Storage.Get<Vote>(VoteKey.KeyName, key);
    }

    public Transaction? GetTransaction(SHA256Hash transactionId)
    {
        var key = new TransactionIdKey(transactionId);
        var txKey = Storage.Get(TransactionIdKey.KeyName, key);

        if (txKey is null)
        {
            return null;
        }

        return Storage.Get<Transaction>(TransactionKey.KeyName, txKey);
    }

    public void Add(Block block)
    {
        var key = new BlockKey(block.GetHash());
        Storage.Put(BlockKey.KeyName, key, Serializer.Serialize(block), CurrentTransaction);
    }

    public void Add(View view)
    {
        var key = new ViewKey(view.Id);
        Storage.Put(ViewKey.KeyName, key, Serializer.Serialize(view), CurrentTransaction);

        var hashKey = new ViewHashKey(view.GetHash());
        Storage.Put(ViewHashKey.KeyName, hashKey, key, CurrentTransaction);
    }

    public void Add(Vote vote)
    {
        var key = new VoteKey(vote.GetHash());
        Storage.Put(VoteKey.KeyName, key, Serializer.Serialize(vote), CurrentTransaction);
    }

    public void Add(Transaction tx)
    {
        if (tx.Id == 0)
        {
            tx.Id = (long)Storage.NextKey();
        }

        // Transaction
        var key = new TransactionKey(tx.Id);
        Storage.Put(TransactionKey.KeyName, key, Serializer.Serialize(tx), CurrentTransaction);

        //ixTransactionId index
        var txId = new TransactionIdKey(tx.CalculateHash());
        Storage.Put(TransactionIdKey.KeyName, txId, key, CurrentTransaction);

        // Address index
        if (tx.PublicKey is not null)
        {
            var addrKey = new TransactionAddressKey(tx.From, key);
            Storage.Put(TransactionAddressKey.KeyName, addrKey, key, CurrentTransaction);
        }

        if (tx.To is not null)
        {
            var addrKey = new TransactionAddressKey(tx.To, key);
            Storage.Put(TransactionAddressKey.KeyName, addrKey, key, CurrentTransaction);
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
        return Storage.GetMany<Block>(BlockKey.KeyName, blockhashes.Select(x => x.Buffer).ToArray());
    }

    public List<Vote> GetVotes(List<SHA256Hash> votehashes)
    {
        return Storage.GetMany<Vote>(VoteKey.KeyName, votehashes.Select(x => x.Buffer).ToArray());
    }

    public List<Transaction> GetTransactions(List<SHA256Hash> transactionIds)
    {
        if (transactionIds.Count == 0)
        {
            return new();
        }

        var keys = Storage.GetMany(TransactionIdKey.KeyName, transactionIds.Select(x => x.Buffer).ToArray());
        return Storage.GetMany<Transaction>(TransactionKey.KeyName, keys);
    }

    public View? GetViewAt(long height)
    {
        var key = new ViewKey(height);
        return Storage.Get<View>(ViewKey.KeyName, key);
    }

    public View? GetLastView()
    {
        return Storage.FindLast<View>(ViewKey.KeyName);
    }

    public long GetLastHeightContainingBlock()
    {
        using var opts = new ReadOptions();
        using var iterator = Storage.GetIterator(ViewKey.KeyName, opts);

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
        var key = new ViewKey(height);
        var view = Storage.Get<View>(ViewKey.KeyName, key);

        if (view is null)
        {
            return new();
        }

        var transactions = new List<Transaction>(view.Transactions.Count + view.Rewards.Count);

        if (view.Transactions.Count > 0)
        {
            var keys = Storage.GetMany(TransactionIdKey.KeyName, view.Transactions.Select(x => x.Buffer).ToArray());
            transactions.AddRange(Storage.GetMany<Transaction>(TransactionKey.KeyName, keys));
        }

        if (view.Rewards.Count > 0)
        {
            var keys = Storage.GetMany(TransactionIdKey.KeyName, view.Rewards.Select(x => x.Buffer).ToArray());
            transactions.AddRange(Storage.GetMany<Transaction>(TransactionKey.KeyName, keys));
        }

        return transactions;
    }

    public List<Vote> GetVotesAtHeight(long height)
    {
        var key = new ViewKey(height);
        var view = Storage.Get<View>(ViewKey.KeyName, key);

        if (view is null || view.Votes.Count == 0)
        {
            return new();
        }

        var keys = view.Votes.Select(x => x.Buffer).ToArray();
        return Storage.GetMany<Vote>(VoteKey.KeyName, keys);
    }

    public ChainState? GetChainState()
    {
        return Storage.FindLast<ChainState>(ChainStateKey.KeyName);
    }

    public ChainState? GetChainState(long height)
    {
        var key = new ChainStateKey(height);
        return Storage.Get<ChainState>(ChainStateKey.KeyName, key);
    }

    public void SaveState(ChainState chainState)
    {
        var key = new ChainStateKey(chainState.Id);
        Storage.Put(ChainStateKey.KeyName, key, chainState, CurrentTransaction);
    }

    public List<Transaction> GetLastNTransctions(int count)
    {
        return Storage.FindLast<Transaction>(TransactionKey.KeyName, count);
    }

    public List<Transaction> GetLastNTransctions(Address address, int count)
    {
        var ids = Storage.FindLast("ixTransactionAddress", address, 5);

        if (ids is null || ids.Count == 0)
        {
            return new();
        }

        return Storage.GetMany<Transaction>(TransactionKey.KeyName, ids.ToArray());
    }

    public Ledger? GetWallet(Address address)
    {
        return Storage.FindLast<Ledger>(LedgerKey.KeyName, address);
    }

    public void UpdateWallet(long height, Ledger ledger)
    {
        var key = new LedgerKey(height, ledger.Address);
        Storage.Put(LedgerKey.KeyName, key, ledger, CurrentTransaction);
    }

    public void UpdateWallets(long height, IEnumerable<Ledger> ledgers)
    {
        foreach (var ledger in ledgers)
        {
            UpdateWallet(height, ledger);
        }
    }

    public void DeleteWallet(long height, Address address)
    {
        var key = new LedgerKey(height, address);
        Storage.Delete(LedgerKey.KeyName, key, CurrentTransaction);
    }

    public Contract? GetContract(Address address)
    {
        return Storage.FindLast<Contract>(ContractKey.KeyName, address);
    }

    public byte[]? GetContractCode(Address address)
    {
        return Storage.FindLast(ContractCodeKey.KeyName, address);
    }

    public byte[]? GetLatestSnapshot(Address address)
    {
        return Storage.FindLast(ContractSnapshotKey.KeyName, address);
    }

    public List<Ledger> GetRichList(int count)
    {
        var results = new Dictionary<Address, Ledger>(count);

        using var opts = new ReadOptions();
        using var iterator = Storage.GetIterator(LedgerKey.KeyName, opts);

        iterator.SeekToLast();

        while (iterator.Valid())
        {
            var addr = (Address)iterator.Key().AsSpan()[..Address.ADDRESS_SZ];

            if (!results.ContainsKey(addr))
            {
                var ledger = Serializer.Deserialize<Ledger>(iterator.Value()) ?? throw new Exception("failed to deserialize validator");
                results.Add(addr, ledger);
            }

            iterator.Prev();
        }

        return results.Values
            .OrderByDescending(x => x.Balance)
            .Take(count)
            .ToList();
    }

    public void AddContract(Contract contract, long height)
    {
        var key = new ContractKey(contract.Address, height);
        Storage.Put(ContractKey.KeyName, key, contract, CurrentTransaction);
    }

    public void AddContractCode(Address contract, long height, byte[] code)
    {
        var key = new ContractCodeKey(contract, height);
        Storage.Put(ContractCodeKey.KeyName, key, code, CurrentTransaction);
    }

    public void AddContractSnapshot(Address contract, long height, byte[] snapshot)
    {
        var key = new ContractSnapshotKey(contract, height);
        Storage.Put(ContractSnapshotKey.KeyName, key, snapshot, CurrentTransaction);
    }

    public void SetToken(Token token, long height)
    {
        if (token.Id == 0)
        {
            token.Id = (long)Storage.NextKey(CurrentTransaction);
        }

        var key = new TokenKey(token.Id, height);
        Storage.Put(TokenKey.KeyName, key, Serializer.Serialize(token), CurrentTransaction);

        // ContractAddress_TokenId
        var tokenIx = new TokenIdKey(token.Contract, token.TokenId, height);
        Storage.Put(TokenIdKey.KeyName, tokenIx, key, CurrentTransaction);

        // LedgerAddress_Key
        var ledgerIx = new TokenLedgerKey(token.Ledger, token.Id, height);
        Storage.Put(TokenLedgerKey.KeyName, ledgerIx, key, CurrentTransaction);
    }

    public void DeleteToken(Token token, long height)
    {
        var key = new TokenKey(token.Id, height);
        Storage.Delete(TokenKey.KeyName, key, CurrentTransaction);

        // ContractAddress_TokenId
        var tokenIx = new TokenIdKey(token.Contract, token.TokenId, height);
        Storage.Delete(TokenIdKey.KeyName, tokenIx, CurrentTransaction);

        // LedgerAddress_Key
        var ledgerIx = new TokenLedgerKey(token.Ledger, token.Id, height);
        Storage.Delete(TokenLedgerKey.KeyName, ledgerIx, CurrentTransaction);
    }

    public List<Transaction> GetTransactions(Address address)
    {
        var ids = Storage.FindLast(TransactionAddressKey.KeyName, address, -1);
        return Storage.GetMany<Transaction>(TransactionKey.KeyName, ids.ToArray());
    }

    public Token? GetToken(Address contract, SHA256Hash tokenId)
    {
        var id = new byte[58];

        // ContractAddress_TokenId
        contract.Buffer.CopyTo(id, 0);
        tokenId.Buffer.CopyTo(id, Address.ADDRESS_SZ);

        var key = Storage.FindLast(TokenIdKey.KeyName, id);

        if (key is null)
        {
            return null;
        }

        return Storage.Get<Token>(TokenKey.KeyName, key);
    }

    public List<Token> GetTokens(Address ledger)
    {
        var tokens = new Dictionary<long, Token>();

        var start = new TokenLedgerKey(ledger, 0, 0);
        ((Span<byte>)start)[Address.ADDRESS_SZ..].Fill(byte.MaxValue);

        var lowerBound = new TokenLedgerKey(ledger, 0, 0);
        ((Span<byte>)lowerBound)[Address.ADDRESS_SZ..].Clear();

        using var opts = new ReadOptions();
        // TODO: Span overload
        opts.IterateLowerBound(((Span<byte>)lowerBound).ToArray());
        
        using var iterator = Storage.GetIterator(TokenLedgerKey.KeyName, opts);

        iterator.SeekForPrev(start);

        while (iterator.Valid())
        {
            var span = iterator.Key().AsSpan();

            var bytes = span.Slice(Address.ADDRESS_SZ, sizeof(long));
            var id = BitConverter.ToInt64(bytes);

            if (!tokens.ContainsKey(id))
            {
                var token = Storage.FindLast<Token>(TokenKey.KeyName, bytes) ?? throw new Exception("invalid index entry to token");
                tokens.Add(id, token);
            }

            iterator.Prev();
        }

        return tokens.Values
            .ToList();
    }

    public List<Token> GetContractTokens(Address contractAddress)
    {
        var tokens = new Dictionary<long, Token>();

        var start = new TokenIdKey(contractAddress, SHA256Hash.NULL_HASH, 0);
        ((Span<byte>)start)[Address.ADDRESS_SZ..].Fill(byte.MaxValue);

        var lowerBound = new TokenIdKey(contractAddress, SHA256Hash.NULL_HASH, 0);
        ((Span<byte>)lowerBound)[Address.ADDRESS_SZ..].Clear();

        using var opts = new ReadOptions();
        // TODO: Span overload
        opts.IterateLowerBound(((Span<byte>)lowerBound).ToArray());
        
        using var iterator = Storage.GetIterator(TokenIdKey.KeyName, opts);

        iterator.SeekForPrev(start);

        while (iterator.Valid())
        {
            var span = iterator.Key().AsSpan();

            var bytes = span.Slice(Address.ADDRESS_SZ, sizeof(long));
            var id = BitConverter.ToInt64(bytes);

            if (!tokens.ContainsKey(id))
            {
                var token = Storage.FindLast<Token>(TokenKey.KeyName, bytes) ?? throw new Exception("invalid index entry to token");
                tokens.Add(id, token);
            }

            iterator.Prev();
        }

        return tokens.Values
            .ToList();
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

        var validator = GetValidator(address);

        if (validator is null)
        {
            return false;
        }

        return validator.Stake >= Constant.MIN_STAKE;
    }

    public Validator? GetValidator(Address address)
    {
        return Storage.FindLast<Validator>(ValidatorKey.KeyName, address);
    }

    public void SetValidator(long height, Validator validator)
    {
        var key = new ValidatorKey(height, validator.NodeAddress);
        Storage.Put(ValidatorKey.KeyName, key, validator, CurrentTransaction);
    }

    public void DeleteValidator(long height, Address address)
    {
        var key = new ValidatorKey(height, address);
        Storage.Delete(ValidatorKey.KeyName, key, CurrentTransaction);
    }

    public List<Validator> GetValidators()
    {
        var validators = new ValidatorCache();

        using var opts = new ReadOptions();
        using var iterator = Storage.GetIterator(ValidatorKey.KeyName, opts);

        iterator.SeekToLast();

        while (iterator.Valid())
        {
            var addr = (Address)iterator.Key().AsSpan()[..Address.ADDRESS_SZ];

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

    public List<Contract> GetContracts()
    {
        var contracts = new Dictionary<Address, Contract>();

        using var opts = new ReadOptions();
        using var iterator = Storage.GetIterator(ContractKey.KeyName, opts);

        iterator.SeekToLast();

        while (iterator.Valid())
        {
            var addr = (Address)iterator.Key().AsSpan()[..Address.ADDRESS_SZ];

            if (!contracts.ContainsKey(addr))
            {
                var contract = Serializer.Deserialize<Contract>(iterator.Value()) ?? throw new Exception("failed to deserialize contract");
                contracts.Add(addr, contract);
            }

            iterator.Prev();
        }

        return contracts.Values
            .ToList();
    }

    public void AddDueTransaction(Transaction tx)
    {
        var txKey = new TransactionKey(tx.Id);
        var key = new ScheduledTransactionKey(tx.Timestamp, tx.Id);
        Storage.Put(ScheduledTransactionKey.KeyName, key, txKey, CurrentTransaction);
    }

    public List<Transaction> GetDueTransactions(long timestamp, bool delete)
    {
        var ts = timestamp.ToKey();

        Span<byte> upperBound = stackalloc byte[sizeof(long) * 2]; // timestamp + id
        upperBound.Fill(255);
        ts.CopyTo(upperBound);

        using var opts = new ReadOptions();
        opts.IterateUpperBound(upperBound.ToArray());

        using var iterator = Storage.GetIterator(ScheduledTransactionKey.KeyName, opts);

        iterator.SeekToFirst();

        var results = new List<Transaction>();

        while (iterator.Valid())
        {
            var id = iterator.Value();
            var tx = Storage.Get<Transaction>(TransactionKey.KeyName, id);

            if (delete)
            {
                Storage.Delete(ScheduledTransactionKey.KeyName, iterator.Key(), CurrentTransaction);
            }

            iterator.Next();

            if (tx is not null)
            {
                results.Add(tx);
            }
        }

        return results;
    }

    public List<Transaction> GetTransactions(int pageNum, int pageSize)
    {
        return Storage.GetRange<Transaction>(TransactionKey.KeyName, pageNum, pageSize);
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

    public void DeleteStore(string storeName)
    {
        var dataDir = Configuration?.GetValue<string>("data-dir") ?? Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".kryolite");
        var stagingStore = Path.Combine(dataDir, $"store.{storeName}");

        Directory.Delete(stagingStore, true);
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

        using var iterator = Storage.GetIterator(TransactionAddressKey.KeyName, opts);

        iterator.SeekForPrev(key);

        var results = new List<Transaction>(count);

        while (iterator.Valid())
        {
            var id = iterator.Value();
            var tx = Storage.Get<Transaction>(TransactionKey.KeyName, id);

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
        var key = new BlockKey(blockhash);
        Storage.Delete(BlockKey.KeyName, key, CurrentTransaction);
    }

    public void DeleteVote(SHA256Hash votehash)
    {
        var key = new VoteKey(votehash);
        Storage.Delete(VoteKey.KeyName, key, CurrentTransaction);
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
        var key = new ViewKey(view.Id);
        Storage.Delete(ViewKey.KeyName, key, CurrentTransaction);

        var hashKey = new ViewHashKey(view.GetHash());
        Storage.Delete(ViewHashKey.KeyName, hashKey, CurrentTransaction);
    }

    public void Delete(Transaction tx)
    {
        // Transaction
        var key = new TransactionKey(tx.Id);
        Storage.Delete(TransactionKey.KeyName, key, CurrentTransaction);

        // transactionId
        var txId = new TransactionIdKey(tx.CalculateHash());
        Storage.Delete(TransactionIdKey.KeyName, txId, CurrentTransaction);

        // Address index
        if (tx.PublicKey is not null)
        {
            var addrKey = new TransactionAddressKey(tx.From, key);
            Storage.Delete(TransactionAddressKey.KeyName, addrKey, CurrentTransaction);
        }

        if (tx.To is not null)
        {
            var addrKey = new TransactionAddressKey(tx.To, key);
            Storage.Delete(TransactionAddressKey.KeyName, addrKey, CurrentTransaction);
        }

        var tsKey = new ScheduledTransactionKey(tx.Timestamp, tx.Id);
        Storage.Delete(ScheduledTransactionKey.KeyName, tsKey, CurrentTransaction);
    }

    public void DeleteContract(Address contract, long height)
    {
        var key = new ContractKey(contract, height);
        Storage.Delete(ContractKey.KeyName, key, CurrentTransaction);
    }

    public void DeleteContractCode(Address contract, long height)
    {
        var key = new ContractCodeKey(contract, height);
        Storage.Delete(ContractCodeKey.KeyName, key, CurrentTransaction);
    }

    public void DeleteContractSnapshot(Address contract, long height)
    {
        var key = new ContractSnapshotKey(contract, height);
        Storage.Delete(ContractSnapshotKey.KeyName, key, CurrentTransaction);
    }

    public void DeleteState(long height)
    {
        var key = new ChainStateKey(height);
        Storage.Delete(ChainStateKey.KeyName, key, CurrentTransaction);
    }

    public void DeleteFromIndexAfterHeight(string ixName, long targetHeight)
    {
        using var opts = new ReadOptions();
        using var iterator = Storage.GetIterator(ixName, opts);

        iterator.SeekToLast();

        while (iterator.Valid())
        {
            var key = iterator.Key();
            
            var heightBytes = key.AsSpan()[^8..].ToArray();
            Array.Reverse(heightBytes);

            var height = BitConverter.ToInt64(heightBytes);

            if (height > targetHeight)
            {
                Storage.Delete(ixName, key, CurrentTransaction);
            }

            iterator.Prev();
        }
    }

    public void DeleteNonLatestFromIndexBeforeHeight(string ixName, long targetHeight)
    {
        using var opts = new ReadOptions();
        using var iterator = Storage.GetIterator(ixName, opts);

        iterator.SeekToLast();

        var prev = Span<byte>.Empty;

        while (iterator.Valid())
        {
            var key = iterator.Key();
            var span = key.AsSpan();
            
            var heightBytes = span[^8..].ToArray();
            Array.Reverse(heightBytes);

            var height = BitConverter.ToInt64(heightBytes);

            if (height < targetHeight)
            {
                var baseKey = key.AsSpan()[..^8];

                if (prev.SequenceEqual(baseKey))
                {
                    Storage.Delete(ixName, key, CurrentTransaction);
                }
                else
                {
                    prev = baseKey;
                }
            }

            iterator.Prev();
        }
    }

    public void Compact()
    {
        Storage.Compact();
    }
}
