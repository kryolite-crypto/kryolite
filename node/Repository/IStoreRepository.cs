using Kryolite.Shared;
using Kryolite.Shared.Blockchain;

namespace Kryolite.Node.Repository;

public interface IStoreRepository
{
    bool Exists(SHA256Hash transactionId);
    Transaction? Get(SHA256Hash transactionId);
    List<Transaction> GetPending();
    void Add(Transaction tx);
    Genesis? GetGenesis();
    View? GetLastView();
    List<Vote> GetVotesAtHeight(long height);
    void SaveState(ChainState chainState);
    void Finalize(List<Transaction> transactions);
    // void Delete(Transaction tx);
    // void DeleteContractSnapshot(long height);
    long? GetTimestamp(SHA256Hash transactionId);
    ChainState? GetChainState();
    Ledger? GetWallet(Address address);
    List<Transaction> GetLastNTransctions(Address address, int count);
    void UpdateWallet(Ledger wallet);
    void UpdateWallets(IEnumerable<Ledger> wallets);
    void UpdateWallets(params Ledger[] wallets);
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
}
