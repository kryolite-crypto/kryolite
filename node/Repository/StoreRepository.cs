using Kryolite.Node.Storage;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using MessagePack;
using Microsoft.Extensions.Configuration;
using RocksDbSharp;
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
        var id = Storage.Get("ixViewHash", viewHash);

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
        Storage.Put("Block", block.GetHash(), MessagePackSerializer.Serialize(block), CurrentTransaction);
    }

    public void Add(View view)
    {
        var key = view.Id.ToKey();
        Storage.Put("View", key, MessagePackSerializer.Serialize(view), CurrentTransaction);
        Storage.Put("ixViewHash", view.GetHash(), key, CurrentTransaction);
    }

    public void Add(Vote vote)
    {
        Storage.Put("Vote", vote.GetHash(), MessagePackSerializer.Serialize(vote), CurrentTransaction);
    }

    public void Add(Transaction tx)
    {
        Span<byte> keyBuf = stackalloc byte[34];
        var transactionId = tx.CalculateHash();

        if (tx.Id == 0)
        {
            tx.Id = (long)Storage.NextKey();
        }

        var key = tx.Id.ToKey();

        // Transaction
        Storage.Put("Transaction", key, MessagePackSerializer.Serialize(tx), CurrentTransaction);

        //ixTransactionId index
        Storage.Put("ixTransactionId", transactionId.Buffer, key, CurrentTransaction);

        // Address index
        var addrKey = keyBuf.Slice(0, 34);
        key.CopyTo(addrKey.Slice(26));

        if (tx.PublicKey is not null)
        {
            tx.From!.Buffer.CopyTo(addrKey);
            Storage.Put("ixTransactionAddress", addrKey, key, CurrentTransaction);
        }

        if (tx.To is not null)
        {
            tx.To.Buffer.CopyTo(addrKey);
            Storage.Put("ixTransactionAddress", addrKey, key, CurrentTransaction);
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
        return Storage.Get<Contract>("Contract", address);
    }

    public byte[]? GetContractCode(Address address)
    {
        return Storage.Get("ContractCode", address);
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
        Span<byte> keyBuf = stackalloc byte[84];

        if (token.Id == 0)
        {
            token.Id = Storage.NextKey(CurrentTransaction);
        }

        var id = BitConverter.GetBytes(token.Id);
        Storage.Put("Token", id, token, CurrentTransaction);

        // ContractAddress_TokenId
        var tokenIx = keyBuf.Slice(0, 58);
        token.Contract.Buffer.CopyTo(tokenIx);
        token.TokenId.Buffer.CopyTo(tokenIx.Slice(26));

        Storage.Put("ixTokenId", tokenIx, id, CurrentTransaction);

        // LedgerAddress_Key
        var ledgerIx = keyBuf.Slice(0, 34);
        token.Ledger.Buffer.CopyTo(ledgerIx);
        id.CopyTo(ledgerIx.Slice(26));

        Storage.Put("ixTokenLedger", ledgerIx, id, CurrentTransaction);
    }

    public void UpdateToken(Token token)
    {
        Span<byte> keyBuf = stackalloc byte[34];

        var id = BitConverter.GetBytes(token.Id);
        var oldToken = Storage.Get<Token>("Token", id);

        if (oldToken is not null)
        {
            // LedgerAddress_Key
            token.Ledger.Buffer.CopyTo(keyBuf);
            id.CopyTo(keyBuf.Slice(26));

            Storage.Delete("ixTokenLedger", keyBuf, CurrentTransaction);
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
        token.Contract.Buffer.AsReadOnlySpan().CopyTo(tokenIx);
        token.TokenId.Buffer.AsReadOnlySpan().CopyTo(tokenIx.Slice(26));

        Storage.Delete("ixTokenId", tokenIx, CurrentTransaction);

        // LedgerAddress_Key
        var ledgerIx = keyBuf.Slice(0, 34);
        token.Ledger.Buffer.AsReadOnlySpan().CopyTo(ledgerIx);
        id.CopyTo(ledgerIx.Slice(26));

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
        tokenId.Buffer.CopyTo(id, 26);

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
        return Storage.GetMany<Token>("Token", keys.ToArray());
    }

    public List<Token> GetContractTokens(Address contractAddress)
    {
        return Storage.FindAll<Token>("Token", contractAddress.Buffer);
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

        var stake = Storage.FindLast<Validator>("Validator", address.Buffer);

        if (stake == null)
        {
            return false;
        }

        return stake.Stake >= Constant.MIN_STAKE;
    }

    public Validator? GetStake(Address address)
    {
        return Storage.FindLast<Validator>("Validator", address.Buffer);
    }

    public void SetStake(Address address, Validator stake, long height)
    {
        Span<byte> keyBuf = stackalloc byte[address.Buffer.Length + sizeof(long)];
        address.Buffer.CopyTo(keyBuf);
        height.ToKey().CopyTo(keyBuf.Slice(Address.ADDRESS_SZ));
        Storage.Put("Validator", keyBuf, stake, CurrentTransaction);
    }

    public void DeleteStake(Address address, long height)
    {
        Span<byte> keyBuf = stackalloc byte[address.Buffer.Length + sizeof(long)];
        address.Buffer.CopyTo(keyBuf);
        height.ToKey().CopyTo(keyBuf.Slice(Address.ADDRESS_SZ));
        Storage.Delete("Validator", keyBuf, CurrentTransaction);
    }

    public List<Validator> GetValidators()
    {
        var validators = Storage.GetAll<Validator>("Validator");

        return validators
            .Where(x => Constant.SEED_VALIDATORS.Contains(x.NodeAddress) || x.Stake >= Constant.MIN_STAKE)
            .DistinctBy(x => x.NodeAddress)
            .OrderByDescending(x => x.Stake)
            .ToList();
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
        Storage.Put("Vote", votehash, CurrentTransaction);
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

        var key = tx.Id.ToKey();

        // Transaction
        Storage.Delete("Transaction", key, CurrentTransaction);

        // transactionId
        Storage.Delete("ixTransactionId", transactionId.Buffer, CurrentTransaction);

        // Address index
        var addrKey = keyBuf.Slice(0, 34);
        key.CopyTo(addrKey.Slice(26));

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
