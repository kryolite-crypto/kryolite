using DuckDB.NET.Data;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Kryolite.Node.Repository;

public interface IBlockchainRepository
{
    bool Exists(SHA256Hash transactionId);
    Transaction? Get(SHA256Hash transactionId);
    List<Transaction> GetPending();
    List<SHA256Hash> GetParentHashes(SHA256Hash transactionId);
    void Add(Transaction tx);
    Genesis? GetGenesis();
    View? GetLastView();
    List<Vote> GetVotesAtHeight(long height);
    void CreateState(ChainState chainState);
    void SaveState(ChainState chainState);
    // void Delete(Transaction tx);
    // void DeleteContractSnapshot(long height);
    ChainState GetChainState();
    Ledger? GetWallet(Address address);
    public void UpdateStatus(List<Transaction> transactions);
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

    SQLiteTransaction BeginTransaction();
}
