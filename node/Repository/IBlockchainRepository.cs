using DuckDB.NET.Data;
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
    bool Exists(SHA256Hash transactionId);
    Transaction? Get(SHA256Hash transactionId);
    List<Transaction> GetParents(SHA256Hash transactionId);
    List<SHA256Hash> GetParentHashes(SHA256Hash transactionId);
    void Add(Transaction tx);
    Genesis? GetGenesis();
    View GetLastView(bool includeVotes = false);
    List<Vote> GetVotes(SHA256Hash transactionId);
    void SaveState(ChainState chainState);
    // void Delete(Transaction tx);
    // void DeleteContractSnapshot(long height);
    ChainState GetChainState();
    Ledger? GetWallet(Address address);
    void UpdateHeight(Transaction transaction);
    void UpdateWallet(Ledger wallet);
    void UpdateWallets(IEnumerable<Ledger> wallets);
    void UpdateWallets(params Ledger[] wallets);
    void AddVote(Vote vote);
    bool VoteExists(Signature signature);
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

    DuckDBTransaction BeginTransaction();
}
