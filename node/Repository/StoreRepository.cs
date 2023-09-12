using Kryolite.Node.Storage;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using MessagePack;
using System.Buffers;

namespace Kryolite.Node.Repository;

public class StoreRepository : IStoreRepository, IDisposable
{
    private IStorage Storage { get; set; }

    public StoreRepository(IStorage storage)
    {
        Storage = storage ?? throw new ArgumentNullException(nameof(storage));
    }

    public void AddRange(List<Transaction> transactions)
    {
        foreach (var tx in transactions)
        {
            Add(tx);
        }
    }

    public void Add(Transaction tx)
    {
        var newEntity = tx.Id == 0;

        var keyBuf = ArrayPool<byte>.Shared.Rent(34);
        var keyMem = keyBuf.AsSpan();

        if (newEntity)
        {
            tx.Id = Storage.NextKey(CurrentTransaction);
        }

        var id = BitConverter.GetBytes(tx.Id);

        // Transaction
        Storage.Put("Transaction", id, MessagePackSerializer.Serialize(tx), CurrentTransaction);

        // TransactionId index
        Storage.Put("ixTransactionId", tx.TransactionId.Buffer, id, CurrentTransaction);

        // Address index
        var addrKey = keyMem.Slice(0, 34);
        id.CopyTo(addrKey.Slice(26));

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

        // height index
        var heightKey = keyMem.Slice(0, 17);

        var height = BitConverter.GetBytes(((ulong?)tx.Height) ?? 0UL);
        Array.Reverse(height);
        height.CopyTo(heightKey);

        heightKey[8] = (byte)tx.TransactionType;

        id.CopyTo(heightKey.Slice(9));

        Storage.Put("ixTransactionHeight", heightKey, id, CurrentTransaction);

        ArrayPool<byte>.Shared.Return(keyBuf);
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
        Array.Reverse(height);

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

    public View? GetViewAt(long height)
    {
        var heightBytes = BitConverter.GetBytes(height);
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

    public List<Transaction> GetTransactionsAtHeight(long height)
    {
        var heightBytes = BitConverter.GetBytes(height);
        Array.Reverse(heightBytes);

        var ids = Storage.FindAll("ixTransactionHeight", heightBytes);

        if (ids.Count == 0)
        {
            return new();
        }

        return Storage.GetMany<Transaction>("Transaction", ids.ToArray());
    }

    public List<Transaction> GetTransactionsAfterHeight(long height)
    {
        var heightBytes = BitConverter.GetBytes(height + 1);
        Array.Reverse(heightBytes);

        var upperBound = new byte[8] { 255, 255, 255, 255, 255, 255, 255, 255 };

        var ids = Storage.FindAll("ixTransactionHeight", heightBytes, upperBound);

        if (ids.Count == 0)
        {
            return new();
        }

        return Storage.GetMany<Transaction>("Transaction", ids.ToArray());
    }

    public List<Vote> GetVotesAtHeight(long height)
    {
        var heightBytes = BitConverter.GetBytes(height);
        Array.Reverse(heightBytes);

        var prefix = new byte[9];

        heightBytes.CopyTo(prefix, 0);
        prefix[8] = (byte)TransactionType.VOTE;

        var upperBound = prefix.ToArray();
        upperBound[8] = (byte)(prefix[8] + 1);

        var voteIds = Storage.FindAll("ixTransactionHeight", prefix, upperBound);

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

    public ChainState? GetChainStateAt(long height)
    {
        var heightKey = BitConverter.GetBytes(height);
        return Storage.Get<ChainState>("ChainStateHistory", heightKey);
    }

    public void SaveState(ChainState chainState)
    {
        var chainKey = new byte[1];
        var heightKey = BitConverter.GetBytes(chainState.Height);

        Storage.Put("ChainState", chainKey, chainState, CurrentTransaction);
        Storage.Put("ChainStateHistory", heightKey, chainState, CurrentTransaction);
    }

    public void DeleteState(long height)
    {
        var chainKey = new byte[1];
        var heightKey = BitConverter.GetBytes(height);

        Storage.Delete("ChainStateHistory", heightKey, CurrentTransaction);
    }

    public void Delete(Transaction tx)
    {
        var id = BitConverter.GetBytes(tx.Id);
        Storage.Delete("Transaction", id, CurrentTransaction);

        Storage.Delete("ixTransactionId", tx.TransactionId.Buffer, CurrentTransaction);

        var keyBuf = ArrayPool<byte>.Shared.Rent(34);
        var keyMem = keyBuf.AsSpan();

        // Address index
        var addrKey = keyMem.Slice(0, 34);
        id.CopyTo(addrKey.Slice(26));

        if (tx.From is not null)
        {
            tx.From.Buffer.CopyTo(addrKey);
            Storage.Delete("ixTransactionAddress", addrKey, CurrentTransaction);
        }

        if (tx.To is not null)
        {
            tx.To.Buffer.CopyTo(addrKey);
            Storage.Delete("ixTransactionAddress", addrKey, CurrentTransaction);
        }

        // Height, TransactionType index
        var height = BitConverter.GetBytes(((ulong?)tx.Height) ?? 0UL);
        Array.Reverse(height);

        var heightKey = keyMem.Slice(0, 17);

        height.CopyTo(heightKey);
        heightKey[8] = (byte)tx.TransactionType;
        id.CopyTo(heightKey.Slice(9));

        Storage.Delete("ixTransactionHeight", heightKey, CurrentTransaction);
    }

    public void DeleteContract(Address address)
    {
        Storage.Delete("Contract", address, CurrentTransaction);
        Storage.Delete("ContractCode", address, CurrentTransaction);
    }

    public void DeleteContractSnapshot(Address address, long height)
    {
        var heightBytes = BitConverter.GetBytes(height).AsSpan();

        var keyBuf = ArrayPool<byte>.Shared.Rent(34);
        var keyMem = keyBuf.AsSpan();

        address.Buffer.CopyTo(keyMem);
        heightBytes.CopyTo(keyMem.Slice(26));

        Storage.Delete("ContractSnapshot", address, CurrentTransaction);
    }

    public List<Transaction> GetLastNTransctions(int count)
    {
        var ids = Storage.FindLast("ixTransactionHeight", count);
        return Storage.GetMany<Transaction>("Transaction", ids.ToArray());
    }

    public List<Transaction> GetLastNTransctions(Address address, int count)
    {
        var ids = Storage.FindLast("ixTransactionAddress", address, 5);

        if (ids is null)
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
            token.Id = Storage.NextKey();
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

        if (token.Id == 0)
        {
            token.Id = Storage.NextKey();
        }

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

        var key = Storage.FindFirst("ixTokenId", id);

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
        return Get(transactionId)?.Timestamp;
    }

    public bool IsValidator(Address address)
    {
        return Storage.Exists("Validator", address.Buffer);
    }

    public Stake? GetStake(Address address)
    {
        return Storage.Get<Stake>("Validator", address.Buffer);
    }

    public void SetStake(Address address, Stake stake)
    {
        Storage.Put("Validator", address.Buffer, stake, CurrentTransaction);
    }

    public void DeleteValidator(Address address)
    {
        Storage.Delete("Validator", address.Buffer, CurrentTransaction);
    }

    public List<Transaction> GetTransactions(int pageNum, int pageSize)
    {
        var ids = Storage.GetRange("ixTransactionHeight", pageNum, pageSize);
        return Storage.GetMany<Transaction>("Transaction", ids.ToArray());
    }

    private ITransaction? CurrentTransaction;

    public ITransaction BeginTransaction()
    {
        if (CurrentTransaction is null || CurrentTransaction.IsDisposed)
        {
            CurrentTransaction = Storage.BeginTransaction();
        }

        return CurrentTransaction;
    }

    public void Dispose()
    {

    }
}
