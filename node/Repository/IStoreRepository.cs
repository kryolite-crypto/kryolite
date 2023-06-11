using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using RocksDbSharp;
using Microsoft.Data.Sqlite;

namespace Kryolite.Node.Repository;

public interface IStoreRepository
{
    bool Exists(SHA256Hash transactionId);
    Transaction? Get(SHA256Hash transactionId);
    List<Transaction> GetPending();
    void Add(Transaction tx, WriteBatch? writeBatch = null);
    Genesis? GetGenesis();
    View? GetLastView();
    List<Vote> GetVotesAtHeight(long height);
    void SaveState(ChainState chainState, WriteBatch? writeBatch = null);
    void Finalize(List<Transaction> transactions, WriteBatch? writeBatch = null);
    // void Delete(Transaction tx);
    // void DeleteContractSnapshot(long height);
    long? GetTimestamp(SHA256Hash transactionId);
    ChainState? GetChainState();
    Ledger? GetWallet(Address address);
    List<Transaction> GetLastNTransctions(Address address, int count);
    void UpdateWallet(Ledger wallet, WriteBatch? writeBatch = null);
    void UpdateWallets(IEnumerable<Ledger> wallets, WriteBatch? writeBatch = null);
    void UpdateWallets(WriteBatch? writeBatch = null, params Ledger[] wallets);
    Contract? GetContract(Address address);
    List<Ledger> GetRichList(int count);
    void AddContract(Contract contract);
    void UpdateContracts(IEnumerable<Contract> contracts);
    void UpdateTokens(IEnumerable<Token> tokens);
    List<Transaction> GetTransactions(Address address);
    List<SHA256Hash> GetTransactionsToValidate();
    Token? GetToken(SHA256Hash tokenId);
    Token? GetToken(Address from, SHA256Hash tokenId);
    List<Token> GetTokens(Address from);
    List<Token> GetContractTokens(Address contractAddress);

    void Write(WriteBatch writeBatch);
}
