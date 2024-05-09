﻿using Kryolite.Node.Storage;
using Kryolite.RocksDb;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;

namespace Kryolite.Node.Repository;

public interface IStoreRepository
{
    bool BlockExists(SHA256Hash transactionId);
    bool VoteExists(SHA256Hash transactionId);
    bool TransactionExists(SHA256Hash transactionId);
    View? GetView(long height);
    View? GetView(SHA256Hash viewHash);
    Block? GetBlock(SHA256Hash blockhash);
    Vote? GetVote(SHA256Hash votehash);
    Transaction? GetTransaction(SHA256Hash transactionId);
    List<Block> GetBlocks(List<SHA256Hash> blockhashes);
    List<Vote> GetVotes(List<SHA256Hash> votehashes);
    List<Transaction> GetTransactions(List<SHA256Hash> transactionIds);
    void Add(View view);
    void Add(Block block);
    void Add(Vote vote);
    void Add(Transaction transaction);
    void AddRange(List<Block> blocks);
    void AddRange(List<Vote> votes);
    void AddRange(List<Transaction> transactions);
    View? GetLastView();
    View? GetViewAt(long height);
    List<Transaction> GetTransactionsAtHeight(long height);
    List<Transaction> GetTransactions(int count, int toSkip);
    List<Vote> GetVotesAtHeight(long height);
    void SaveState(ChainState chainState);
    long? GetTimestamp(SHA256Hash transactionId);
    ChainState? GetChainState();
    ChainState? GetChainState(long height);
    Ledger? GetWallet(Address address);
    List<Transaction> GetLastNTransctions(int count);
    List<Transaction> GetLastNTransctions(Address address, int count);
    void UpdateWallet(Ledger wallet);
    void UpdateWallets(IEnumerable<Ledger> wallets);
    void UpdateWallets(params Ledger[] wallets);
    Contract? GetContract(Address address);
    byte[]? GetContractCode(Address address);
    byte[]? GetLatestSnapshot(Address address);
    List<Ledger> GetRichList(int count);
    void AddContract(Contract contract);
    void AddContractCode(Address contract, byte[] code);
    void AddContractSnapshot(Address contract, long height, byte[] snapshot);
    void UpdateContract(Contract contract);
    void UpdateContracts(IEnumerable<Contract> contracts);
    void AddToken(Token token);
    void UpdateToken(Token token);
    void UpdateTokens(IEnumerable<Token> tokens);
    void DeleteToken(Token token);
    List<Transaction> GetTransactions(Address address);
    List<Transaction> GetVotesForAddress(Address address, int count);
    Token? GetToken(Address contract, SHA256Hash tokenId);
    List<Token> GetTokens(Address from);
    List<Token> GetContractTokens(Address contractAddress);
    ITransaction BeginTransaction();
    bool IsValidator(Address address);
    Validator? GetStake(Address address);
    void SetStake(Address address, Validator stake);
    void DeleteStake(Address address);
    List<Validator> GetValidators();
    void Reset();
    void ReplaceDbFrom(string storeName);
    void Close();
    Checkpoint CreateCheckpoint();
    void DeleteBlock(SHA256Hash blockhash);
    void DeleteVote(SHA256Hash votehash);
    void DeleteBlocks(List<SHA256Hash> blockhashes);
    void DeleteVotes(List<SHA256Hash> votehashes);
    void Delete(View view);
    void Delete(Transaction tx);
    void DeleteContract(Address contract);
    void DeleteContractCode(Address contract);
    void DeleteContractSnapshot(Address contract, long height);
    void DeleteState(long height);
    List<Transaction> GetDueTransactions(long timestamp, bool delete);
    void AddDueTransaction(Transaction tx);
    long GetLastHeightContainingBlock();
    void Compact();
}
