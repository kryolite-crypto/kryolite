using Kryolite.Node.Storage;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using System.Numerics;

namespace Kryolite.Node.Repository;

public interface IStoreRepository
{
    bool Exists(SHA256Hash transactionId);
    Transaction? Get(SHA256Hash transactionId);
    List<Transaction> GetPending();
    void AddRange(List<Transaction> transactions);
    void Delete(Transaction tx);
    Genesis? GetGenesis();
    View? GetLastView();
    View? GetViewAt(long height);
    List<Transaction> GetTransactionsAtHeight(long height);
    List<Transaction> GetTransactionsAfterHeight(long height);
    List<Vote> GetVotesAtHeight(long height);
    void SaveState(ChainState chainState);
    void DeleteState(long height);
    long? GetTimestamp(SHA256Hash transactionId);
    ChainState? GetChainState();
    ChainState? GetChainStateAt(long height);
    Ledger? GetWallet(Address address);
    List<Transaction> GetLastNTransctions(int count);
    List<Transaction> GetLastNTransctions(Address address, int count);
    void UpdateWallet(Ledger wallet);
    void UpdateWallets(IEnumerable<Ledger> wallets);
    void UpdateWallets(params Ledger[] wallets);
    Contract? GetContract(Address address);
    byte[]? GetContractCode(Address address);
    ContractSnapshot? GetLatestSnapshot(Address address);
    List<Ledger> GetRichList(int count);
    void AddContract(Contract contract);
    void AddContractSnapshot(Address contract, ContractSnapshot snapshot);
    void UpdateContract(Contract contract);
    void DeleteContract(Address address);
    void DeleteContractSnapshot(Address address, long height);
    void UpdateContracts(IEnumerable<Contract> contracts);
    void AddToken(Token token);
    void UpdateToken(Token token);
    void UpdateTokens(IEnumerable<Token> tokens);
    void DeleteToken(Token token);
    List<Transaction> GetTransactions(Address address);
    Token? GetToken(Address contract, SHA256Hash tokenId);
    List<Token> GetTokens(Address from);
    List<Token> GetContractTokens(Address contractAddress);
    ITransaction BeginTransaction();
    bool IsValidator(Address address);
    long GetStake(Address address);
    void SetStake(Address address, long stake);
    void DeleteValidator(Address address);
}
