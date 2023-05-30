using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Kryolite.Node.Repository;

public interface IBlockchainRepository
{
    DbContext GetContext();
    long Count();
    T? Get<T>(SHA256Hash transactionId) where T : Transaction;
    T? Get<T>(long height) where T : Transaction;
    void Add<T>(T tx) where T : Transaction;
    Genesis? GetGenesis();
    View GetLastView();
    List<Vote> GetVotes(SHA256Hash transactionId);
    void SaveState(ChainState chainState);
    List<Block> GetBlocks(long height);
    void Delete(Transaction tx);
    void DeleteContractSnapshot(long height);
    ChainState GetChainState();
    LedgerWallet? GetWallet(Address address);
    void UpdateWallet(LedgerWallet wallet);
    void UpdateWallets(IEnumerable<LedgerWallet> wallets);
    void UpdateWallets(params LedgerWallet[] wallets);
    void AddVote(Vote vote);
    void AddVote(List<Vote> votes);
    bool VoteExists(Signature signature);
    Contract? GetContract(Address address, bool noCode = false);
    List<LedgerWallet> GetRichList(int count);
    void AddContract(Contract contract);
    void UpdateContracts(IEnumerable<Contract> contracts);
    void UpdateTokens(IEnumerable<Token> tokens);
    List<Transaction> GetTransactions(Address address);
    List<Transaction> GetTransactionsToValidate();
    Token? GetToken(SHA256Hash tokenId);
    Token? GetToken(Address from, SHA256Hash tokenId);
    List<Token> GetTokens(Address from);
    List<Token> GetContractTokens(Address contractAddress);
}
