using Kryolite.Node.Storage;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using MessagePack;
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
        var storage = new RocksDBStorage(storePath, true);
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
        var id = BitConverter.GetBytes(height);
        Array.Reverse(id);

        return Storage.Get<View>("View", id, CurrentTransaction);
    }

    public View? GetView(SHA256Hash viewHash)
    {
        var id = Storage.Get("ixViewHash", viewHash, CurrentTransaction);

        if (id is null)
        {
            return null;
        }

        return Storage.Get<View>("View", id, CurrentTransaction);
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
        return Storage.Get<Transaction>("Transaction", transactionId);
    }

    public void Add(Block block)
    {
        Storage.Put("Block", block.GetHash(), MessagePackSerializer.Serialize(block), CurrentTransaction);
    }

    public void Add(View view)
    {
        var id = BitConverter.GetBytes(view.Id);
        Array.Reverse(id);

        Storage.Put("View", id, MessagePackSerializer.Serialize(view), CurrentTransaction);
        Storage.Put("ixViewHash", view.GetHash(), id, CurrentTransaction);
    }

    public void Add(Vote vote)
    {
        Storage.Put("Vote", vote.GetHash(), MessagePackSerializer.Serialize(vote), CurrentTransaction);
    }

    public void Add(Transaction tx)
    {
        var keyBuf = ArrayPool<byte>.Shared.Rent(34);
        var keyMem = keyBuf.AsSpan();
        var transactionId = tx.CalculateHash();

        // Transaction
        Storage.Put("Transaction", transactionId, MessagePackSerializer.Serialize(tx), CurrentTransaction);

        var num = BitConverter.GetBytes(Storage.NextKey());
        Array.Reverse(num);

        //ixTransactionNum index
        Storage.Put("ixTransactionNum", num, transactionId.Buffer, CurrentTransaction);

        // Address index
        var addrKey = keyMem.Slice(0, 34);
        num.CopyTo(addrKey.Slice(26));

        if (tx.PublicKey is not null)
        {
            tx.From!.Buffer.CopyTo(addrKey);
            Storage.Put("ixTransactionAddress", addrKey, transactionId.Buffer, CurrentTransaction);
        }

        if (tx.To is not null)
        {
            tx.To.Buffer.CopyTo(addrKey);
            Storage.Put("ixTransactionAddress", addrKey, transactionId.Buffer, CurrentTransaction);
        }

        ArrayPool<byte>.Shared.Return(keyBuf);
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
        return Storage.GetMany<Transaction>("Transaction", transactionIds.Select(x => x.Buffer).ToArray());
    }

    public View? GetViewAt(long height)
    {
        var heightBytes = BitConverter.GetBytes(height);
        Array.Reverse(heightBytes);

        return Storage.Get<View>("View", heightBytes);
    }

    public View? GetLastView()
    {
        var chainKey = new byte[1];
        var chainState = Storage.Get<ChainState>("ChainState", chainKey);

        var heightBytes = BitConverter.GetBytes(chainState?.Id ?? 0);
        Array.Reverse(heightBytes);

        return Storage.Get<View>("View", heightBytes);
    }

    public List<Transaction> GetTransactionsAtHeight(long height)
    {
        var heightBytes = BitConverter.GetBytes(height);
        Array.Reverse(heightBytes);

        var view = Storage.Get<View>("View", heightBytes);

        if (view is null)
        {
            return new();
        }

        var transactions = new List<Transaction>(view.Transactions.Count + view.Rewards.Count);

        if (view.Transactions.Count > 0)
        {
            transactions.AddRange(Storage.GetMany<Transaction>("Transaction", view.Transactions.Select(x => x.Buffer).ToArray()));
        }

        if (view.Rewards.Count > 0)
        {
            transactions.AddRange(Storage.GetMany<Transaction>("Transaction", view.Rewards.Select(x => x.Buffer).ToArray()));
        }

        return transactions;
    }

    public List<Vote> GetVotesAtHeight(long height)
    {
        var heightBytes = BitConverter.GetBytes(height);
        Array.Reverse(heightBytes);

        var view = Storage.Get<View>("View", heightBytes);

        if (view is null || view.Votes.Count == 0)
        {
            return new();
        }

        var keys = view.Votes.Select(x => x.Buffer).ToArray();
        return Storage.GetMany<Vote>("Vote", keys);
    }

    public ChainState? GetChainState()
    {
        var chainKey = new byte[1];
        return Storage.Get<ChainState>("ChainState", chainKey, CurrentTransaction);
    }

    public void SaveState(ChainState chainState)
    {
        var chainKey = new byte[1];
        Storage.Put("ChainState", chainKey, chainState, CurrentTransaction);
    }

    public List<Transaction> GetLastNTransctions(int count)
    {
        var ids = Storage.FindLast("ixTransactionNum", count);
        return Storage.GetMany<Transaction>("Transaction", ids.ToArray());
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
        return Storage.Get<Ledger>("Ledger", address.Buffer, CurrentTransaction);
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
        var keyBuf = ArrayPool<byte>.Shared.Rent(34);
        contract.Buffer.CopyTo(keyBuf, 0);
        
        var heightBytes = BitConverter.GetBytes(height);
        Array.Reverse(heightBytes);
        heightBytes.CopyTo(keyBuf, 26);

        Storage.Put("ContractSnapshot", keyBuf, snapshot, CurrentTransaction);
        
        ArrayPool<byte>.Shared.Return(keyBuf);
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
        var keyBuf = ArrayPool<byte>.Shared.Rent(84);
        var keyMem = keyBuf.AsSpan();

        if (token.Id == 0)
        {
            token.Id = Storage.NextKey(CurrentTransaction);
        }

        var id = BitConverter.GetBytes(token.Id);
        Storage.Put("Token", id, token, CurrentTransaction);

        // ContractAddress_TokenId
        var tokenIx = keyMem.Slice(58);
        token.Contract.Buffer.AsReadOnlySpan().CopyTo(tokenIx);
        token.TokenId.Buffer.AsReadOnlySpan().CopyTo(tokenIx.Slice(26));

        Storage.Put("ixTokenId", tokenIx, id, CurrentTransaction);

        // LedgerAddress_Key
        var ledgerIx = keyMem.Slice(34);
        token.Ledger.Buffer.AsReadOnlySpan().CopyTo(ledgerIx);
        id.CopyTo(ledgerIx.Slice(26));

        Storage.Put("ixTokenLedger", ledgerIx, id, CurrentTransaction);

        ArrayPool<byte>.Shared.Return(keyBuf);
    }

    public void UpdateToken(Token token)
    {
        var keyBuf = ArrayPool<byte>.Shared.Rent(34);
        var keyMem = keyBuf.AsSpan();

        var id = BitConverter.GetBytes(token.Id);
        var oldToken = Storage.Get<Token>("Token", id);

        if (oldToken is not null)
        {
            // LedgerAddress_Key
            token.Ledger.Buffer.AsReadOnlySpan().CopyTo(keyMem);
            id.CopyTo(keyMem.Slice(26));

            Storage.Delete("ixTokenLedger", keyMem, CurrentTransaction);
        }

        ArrayPool<byte>.Shared.Return(keyBuf);

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
        var keyBuf = ArrayPool<byte>.Shared.Rent(84);
        var keyMem = keyBuf.AsSpan();

        var id = BitConverter.GetBytes(token.Id);
        Storage.Delete("Token", id, CurrentTransaction);

        // ContractAddress_TokenId
        var tokenIx = keyMem.Slice(58);
        token.Contract.Buffer.AsReadOnlySpan().CopyTo(tokenIx);
        token.TokenId.Buffer.AsReadOnlySpan().CopyTo(tokenIx.Slice(26));

        Storage.Delete("ixTokenId", tokenIx, CurrentTransaction);

        // LedgerAddress_Key
        var ledgerIx = keyMem.Slice(34);
        token.Ledger.Buffer.AsReadOnlySpan().CopyTo(ledgerIx);
        id.CopyTo(ledgerIx.Slice(26));

        Storage.Delete("ixTokenLedger", ledgerIx, CurrentTransaction);

        ArrayPool<byte>.Shared.Return(keyBuf);
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

        var key = Storage.Get("ixTokenId", id, CurrentTransaction);

        if (key is null)
        {
            return null;
        }

        return Storage.Get<Token>("Token", key, CurrentTransaction);
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

        var stake = Storage.Get<Validator>("Validator", address.Buffer, CurrentTransaction);

        if (stake == null)
        {
            return false;
        }

        return stake.Stake >= Constant.MIN_STAKE;
    }

    public Validator? GetStake(Address address)
    {
        return Storage.Get<Validator>("Validator", address.Buffer, CurrentTransaction);
    }

    public void SetStake(Address address, Validator stake)
    {
        Storage.Put("Validator", address.Buffer, stake, CurrentTransaction);
    }

    public void DeleteValidator(Address address)
    {
        Storage.Delete("Validator", address.Buffer, CurrentTransaction);
    }

    public List<Validator> GetValidators()
    {
        var validators = Storage.GetAll<Validator>("Validator");

        return validators
            .Where(x => Constant.SEED_VALIDATORS.Contains(x.NodeAddress) || x.Stake >= Constant.MIN_STAKE)
            .OrderByDescending(x => x.Stake)
            .ToList();
    }

    public List<Transaction> GetTransactions(int pageNum, int pageSize)
    {
        var ids = Storage.GetRange("ixTransactionNum", pageNum, pageSize);
        return Storage.GetMany<Transaction>("Transaction", ids.ToArray());
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
        var newStore = Path.Combine(dataDir, $"store.{storeName}");
        var backupStore = Path.Combine(dataDir, $"store.bak");

        Storage.Close();

        if (Directory.Exists(backupStore))
        {
            Directory.Delete(backupStore, true);
        }

        Directory.Move(activeStore, backupStore);
        Directory.Move(newStore, activeStore);

        Storage.Open(activeStore);
    }

    public void Close()
    {
        Storage.Close();
    }

    public void Dispose()
    {

    }
}
