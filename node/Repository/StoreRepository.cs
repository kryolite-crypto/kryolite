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

        var height = BitConverter.GetBytes(((ulong?)tx.Height) ?? ulong.MaxValue);
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
        var height = BitConverter.GetBytes(((ulong?)tx.Height) ?? ulong.MaxValue);
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
        return Storage.Get<byte[]>("Contract", address);
    }

    public ContractSnapshot? GetLatestSnapshot(Address address)
    {
        return Storage.FindLast<ContractSnapshot>("ContractSnapshot", address);
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
        Storage.Put<Contract>("Contract", contract.Address.Buffer, contract, CurrentTransaction);
    }

    public void AddContractSnapshot(Address contract, ContractSnapshot snapshot)
    {
        Storage.Put<ContractSnapshot>("ContractSnapshot", contract.Buffer, snapshot, CurrentTransaction);
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

        var id = keyMem.Slice(58);

        // ContractAddress_TokenId
        token.Contract.Buffer.AsReadOnlySpan().CopyTo(id);
        token.TokenId.Buffer.AsReadOnlySpan().CopyTo(id.Slice(26));

        Storage.Put<Token>("Token", id, token, CurrentTransaction);

        var addrIx = keyMem.Slice(84);

        // OwnerAddress_ContractAddress_TokenId
        token.Ledger.Buffer.AsReadOnlySpan().CopyTo(id);
        token.Contract.Buffer.AsReadOnlySpan().CopyTo(id.Slice(26));
        token.TokenId.Buffer.AsReadOnlySpan().CopyTo(id.Slice(52));

        Storage.Put("ixTokenAddress", addrIx, id.ToArray(), CurrentTransaction);
    }

    public void UpdateToken(Token token)
    {
        AddToken(token);
    }

    public void UpdateTokens(IEnumerable<Token> tokens)
    {
        foreach (var token in tokens)
        {
            AddToken(token);
        }
    }

    public void DeleteToken(Token token)
    {
        var keyBuf = ArrayPool<byte>.Shared.Rent(84);
        var keyMem = keyBuf.AsSpan();

        var id = keyMem.Slice(58);

        // ContractAddress_TokenId
        token.Contract.Buffer.AsReadOnlySpan().CopyTo(id);
        token.TokenId.Buffer.AsReadOnlySpan().CopyTo(id.Slice(26));

        Storage.Delete("Token", id, CurrentTransaction);

        var addrIx = keyMem.Slice(84);

        // OwnerAddress_ContractAddress_TokenId
        token.Ledger.Buffer.AsReadOnlySpan().CopyTo(id);
        token.Contract.Buffer.AsReadOnlySpan().CopyTo(id.Slice(26));
        token.TokenId.Buffer.AsReadOnlySpan().CopyTo(id.Slice(52));

        Storage.Delete("ixTokenAddress", addrIx, CurrentTransaction);
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

    public Token? GetToken(Address contract, SHA256Hash tokenId)
    {
        var id = new byte[58];

        // ContractAddress_TokenId
        contract.Buffer.CopyTo(id, 0);
        tokenId.Buffer.CopyTo(id, 26);

        return Storage.Get<Token>("Token", id);
    }

    public List<Token> GetTokens(Address ledger)
    {
        var keys = Storage.FindAll("ixTokenAddress", ledger.Buffer);
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
