using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using Kryolite.Node.Executor;
using Kryolite.Node.Migrations;
using Kryolite.Node.Repository;
using Kryolite.Node.Services;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using Kryolite.Shared.Dto;
using MessagePack;
using Microsoft.Extensions.Logging;
using NSec.Cryptography;
using Redbus.Interfaces;
using RocksDbSharp;

namespace Kryolite.Node;

public class StoreManager : IStoreManager
{
    private IStoreRepository Repository { get; }
    private IBufferService<TransactionDto, OutgoingTransactionService> TransactionBuffer { get; }
    private IExecutorFactory ExecutorFactory { get; }
    private INetworkManager NetworkManager { get; }
    private IWalletManager WalletManager { get; }
    private IEventBus EventBus { get; }
    private ILogger<StoreManager> Logger { get; }

    private static ReaderWriterLockSlim rwlock = new(LockRecursionPolicy.SupportsRecursion);

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
    private static Dictionary<SHA256Hash, Transaction> PendingCache;
    private static Dictionary<Address, Ledger> LedgerCache;
    private static View CurrentView;
    private static ChainState ChainState;
    private static Wallet Node;
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

    public StoreManager(IStoreRepository repository, IBufferService<TransactionDto, OutgoingTransactionService> transactionBuffer, IExecutorFactory executorFactory, INetworkManager networkManager, IWalletManager walletManager, IEventBus eventBus, ILogger<StoreManager> logger)
    {
        Repository = repository ?? throw new ArgumentNullException(nameof(repository));
        TransactionBuffer = transactionBuffer ?? throw new ArgumentNullException(nameof(transactionBuffer));
        ExecutorFactory = executorFactory ?? throw new ArgumentNullException(nameof(executorFactory));
        NetworkManager = networkManager ?? throw new ArgumentNullException(nameof(networkManager));
        WalletManager = walletManager ?? throw new ArgumentNullException(nameof(walletManager));
        EventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (PendingCache is null)
        {
            using var _ = rwlock.EnterWriteLockEx();
            PendingCache = new();

            var pending = Repository.GetPending();

            foreach (var parent in pending)
            {
                ref var tx = ref CollectionsMarshal.GetValueRefOrAddDefault(PendingCache, parent.TransactionId, out var existed);

                if (!existed)
                {
                    tx = parent;
                }
            }
        }

        if (LedgerCache is null)
        {
            using var _ = rwlock.EnterWriteLockEx();
            LedgerCache = new();
        }

        if (CurrentView is null)
        {
            using var _ = rwlock.EnterWriteLockEx();
            CurrentView = Repository.GetLastView()!;
        }

        if (ChainState is null)
        {
            using var _ = rwlock.EnterWriteLockEx();
            ChainState = Repository.GetChainState()!;
        }

        if (Node is null)
        {
            using var _ = rwlock.EnterWriteLockEx();
            Node = walletManager.GetNodeWallet() ?? throw new ArgumentNullException(nameof(Node));
        }
    }

    public bool AddGenesis(Genesis genesis)
    {
        using var _ = rwlock.EnterWriteLockEx();
        // using var dbtx = Repository.BeginTransaction();

        try
        {
            genesis.TransactionId = genesis.CalculateHash();

            Repository.Add(genesis);

            var chainState = new ChainState
            {
                Id = 0,
                Height = -1,
                LastHash = genesis.TransactionId,
                CurrentDifficulty = BigInteger.Pow(new BigInteger(2), Constant.STARTING_DIFFICULTY).ToDifficulty()
            };

            Repository.SaveState(chainState);

            ChainState = chainState;

            PendingCache.Add(genesis.TransactionId, genesis);

            //dbtx.Commit();
            return true;
        }
        catch (Exception ex)
        {
            //dbtx.Rollback();
            Logger.LogError(ex, "AddGenesis error");
        }

        return false;
    }

    public bool AddView(View view, bool broadcast, bool selfCreated)
    {
        using var _ = rwlock.EnterWriteLockEx();
        //using var dbtx = Repository.BeginTransaction();

        try
        {
            var sw = Stopwatch.StartNew();

            var height = view.Height ?? 0;

            view.TransactionId = view.CalculateHash();

            if (height != ChainState.Height + 1)
            {
                Logger.LogInformation("Discarding view #{height} (reason = invalid height)", view.Height);
                return false;
            }

            var toExecute = new List<Transaction>(PendingCache.Count);

            if (height > 0)
            {
                var earliest = CurrentView.Timestamp + Constant.HEARTBEAT_INTERVAL;

                if (view.Timestamp < earliest)
                {
                    Logger.LogInformation("Discarding view #{height} (reason = timestamp too early)", view.Height);
                    return false;
                }
            }

            var voteCount = 0;
            var blockCount = 0;

            foreach (var parentId in view.Parents)
            {
                if (!PendingCache.Remove(parentId, out var parent))
                {
                    continue;
                }

                if (parent.Height is not null)
                {
                    continue;
                }

                TraverseTransaction(parent, height, toExecute, ref voteCount, ref blockCount);
            }

            var context = new ExecutorContext(Repository, LedgerCache, voteCount, blockCount);
            var executor = ExecutorFactory.Create(context);

            executor.Execute(toExecute);

            if (height > 0)
            {
                if (blockCount == 0)
                {
                    var work = ChainState.CurrentDifficulty.ToWork();
                    var nextTarget = work / 4 * 3;
                    var minTarget = BigInteger.Pow(new BigInteger(2), Constant.STARTING_DIFFICULTY);

                    ChainState.CurrentDifficulty = BigInteger.Max(minTarget, nextTarget).ToDifficulty();
                }
                else
                {
                    var totalWork = ChainState.CurrentDifficulty.ToWork() * blockCount;

                    ChainState.Weight += totalWork;
                    ChainState.CurrentDifficulty = totalWork.ToDifficulty();
                }
            }

            ChainState.Height++;
            ChainState.LastHash = view.TransactionId;
            ChainState.Weight += ChainState.CurrentDifficulty.ToWork() * toExecute.Count;

            if (selfCreated)
            {
                view.Sign(Node.PrivateKey);
            }

            view.ExecutionResult = ExecutionResult.SUCCESS;

            Repository.SaveState(ChainState, context.GetBatch());
            Repository.Add(view, context.GetBatch());
            Repository.Finalize(toExecute, context.GetBatch());
            Repository.Write(context.GetBatch());
            context.GetBatch().Dispose();

            //dbtx.Commit();

            CurrentView = view;

            if (broadcast)
            {
                TransactionBuffer.Add(new TransactionDto(view));
            }

            EventBus.PublishAsync(ChainState);

            foreach (var ledger in LedgerCache.Values)
            {
                EventBus.PublishAsync(ledger);
            }

            LedgerCache = new(); // note: LedgerCache.Clear() has really bad performance here

            sw.Stop();
            Logger.LogInformation($"Added view #{height} in {sw.Elapsed.TotalNanoseconds / 1000000}ms [Transactions = {toExecute.Count}] [Next difficulty = {ChainState.CurrentDifficulty}]");

            return true;
        }
        catch (Exception ex)
        {
            //dbtx.Rollback();
            Logger.LogError(ex, "AddView error");

            PendingCache = new();

            var pending = Repository.GetPending();

            foreach (var parent in pending)
            {
                ref var tx = ref CollectionsMarshal.GetValueRefOrAddDefault(PendingCache, parent.TransactionId, out var existed);

                if (!existed)
                {
                    tx = parent;
                }
            }

            CurrentView = Repository.GetLastView()!;
            ChainState = Repository.GetChainState()!;
        }

        return false;
    }

    public bool AddBlock(Block block, bool broadcast)
    {
        var sw = Stopwatch.StartNew();

        if (block.To is null)
        {
            Logger.LogInformation("AddBlock rejected (reason = null to address)");
            return false;
        }

        if (block.Value != Constant.BLOCK_REWARD)
        {
            Logger.LogInformation("AddBlock rejected (reason = invalid reward)");
            return false;
        }

        if (!block.VerifyNonce())
        {
            Logger.LogInformation("AddBlock rejected (reason = invalid nonce)");
            return false;
        }

        block.TransactionId = block.CalculateHash();

        if (block.Difficulty != ChainState.CurrentDifficulty)
        {
            Logger.LogInformation("AddBlock rejected (reason = invalid difficulty)");
            return false;
        }

        /*if (block.ParentHash != ChainState.LastHash)
        {
            Logger.LogInformation("AddBlock rejected (reason = invalid parent hash)");
            return false;
        }*/

        var exists = Repository.Exists(block.TransactionId);

        if (exists)
        {
            Logger.LogInformation("AddBlock rejected (reason = already exists)");
            return false;
        }

        if (!LedgerCache.TryGetValue(block.To, out var to))
        {
            to = Repository.GetWallet(block.To);

            if (to is null)
            {
                to = new Ledger(block.To);
            }

            LedgerCache.Add(block.To, to);
        }

        checked
        {
            to.Pending += block.Value;
        }

        ChainState.Blocks++;

        Repository.UpdateWallet(to);
        Repository.Add(block);
        Repository.SaveState(ChainState);

        sw.Stop();

        Logger.LogInformation($"Added block #{ChainState.Blocks} in {sw.Elapsed.TotalNanoseconds / 1000000}ms [diff = {block.Difficulty}]");

        if (broadcast)
        {
            TransactionBuffer.Add(new TransactionDto(block));
        }

        PendingCache.Add(block.TransactionId, block);

        return true;
    }

    public bool AddBlock(Blocktemplate blocktemplate, bool broadcast)
    {
        using var _ = rwlock.EnterWriteLockEx();
        //using var dbtx = Repository.BeginTransaction();

        try
        {
            if (blocktemplate.Validates.Count < 2)
            {
                Logger.LogInformation("AddTransaction rejected (reason = not enought transactions referenced)");
                return false;
            }

            foreach (var txhash in blocktemplate.Validates)
            {
                var tx = Repository.Get(txhash);

                if (tx is null)
                {
                    Logger.LogInformation("AddBlock rejected (reason = unknown transaction reference)");
                    return false;
                }

                if (blocktemplate.Timestamp < tx.Timestamp)
                {
                    Logger.LogInformation("AddBlock rejected (reason = invalid timestamp)");
                    return false;
                }
            }

            var block = new Block(blocktemplate.To, blocktemplate.Timestamp, blocktemplate.ParentHash, blocktemplate.Difficulty, blocktemplate.Validates);

            block.Pow = blocktemplate.Solution;

            var newblock = AddBlock(block, broadcast);

            //dbtx.Commit();

            return newblock;
        }
        catch (Exception ex)
        {
            //dbtx.Rollback();
            Logger.LogError(ex, "AddBlock error");
        }

        return false;
    }

    public bool AddTransaction(Transaction tx, bool broadcast, WriteBatch? writeBatch = null)
    {
        try
        {
            //var sw = Stopwatch.StartNew();

            if (tx.TransactionType == TransactionType.PAYMENT && tx.Value == 0)
            {
                Logger.LogInformation("AddTransaction rejected (reason = zero payment)");
                return false;
            }

            if (tx.To is null)
            {
                Logger.LogInformation("AddTransaction rejected (reason = null 'to' address)");
                return false;
            }

            tx.TransactionId = tx.CalculateHash();

            var exists = Repository.Exists(tx.TransactionId);

            if (exists)
            {
                // no need to do anything, we have this already
                return true;
            }

            //sw.Stop();
            //Logger.LogInformation($"AddTransaction.Exists {sw.Elapsed.TotalNanoseconds / 1_000_000}ms");
            //sw.Reset();

            if (!LedgerCache.TryGetValue(tx.From, out var from))
            {
                from = Repository.GetWallet(tx.From) ?? new Ledger(tx.From);
                LedgerCache.Add(tx.From, from);
            }

            if (from.Balance < tx.Value)
            {
                Logger.LogInformation("AddTransaction rejected (reason = too low balance)");
                return false;
            }

            if (!LedgerCache.TryGetValue(tx.To, out var to))
            {
                to = Repository.GetWallet(tx.To) ?? new Ledger(tx.To);
                LedgerCache.Add(tx.To, to);
            }

            //sw.Stop();
            //Logger.LogInformation($"AddTransaction.Wallets {sw.Elapsed.TotalNanoseconds / 1_000_000}ms");
            //sw.Restart();

            checked
            {
                from.Balance -= tx.Value;
                to.Balance += tx.Value;
            }

            writeBatch = new WriteBatch();

            //sw.Stop();
            //Logger.LogInformation($"AddTransaction.Balance {sw.Elapsed.TotalNanoseconds / 1_000_000}ms");
            //sw.Restart();

            Repository.Add(tx, writeBatch);

            //sw.Stop();
            //Logger.LogInformation($"AddTransaction.Add {sw.Elapsed.TotalNanoseconds / 1_000_000}ms");
            //sw.Restart();

            Repository.UpdateWallets(writeBatch, from, to);

            Repository.Write(writeBatch);

            //sw.Stop();
            //Logger.LogInformation($"AddTransaction.Update {sw.Elapsed.TotalNanoseconds / 1_000_000}ms");
            //sw.Restart();

            PendingCache.Add(tx.TransactionId, tx);

            //sw.Stop();
            //Logger.LogInformation($"AddTransaction.Cache {sw.Elapsed.TotalNanoseconds / 1_000_000}ms");
            //sw.Restart();

            EventBus.PublishAsync(from);
            EventBus.PublishAsync(to);

            //sw.Stop();
            //Logger.LogInformation($"AddTransaction.Publish {sw.Elapsed.TotalNanoseconds / 1_000_000}ms");
            //sw.Restart();

            if (broadcast)
            {
                TransactionBuffer.Add(new TransactionDto(tx));
            }

            //sw.Stop();
            //Logger.LogInformation($"AddTransaction.Post {sw.Elapsed.TotalNanoseconds / 1_000_000}ms");

            return true;
        }
        catch (Exception ex) 
        {
            Logger.LogError(ex, "AddTransaction error");
        }

        return false;
    }

    public bool AddTransaction(TransactionDto tx, bool broadcast, WriteBatch? writeBatch = null)
    {
        if (tx.Parents.Count < 2)
        {
            Logger.LogInformation("AddTransaction rejected (reason = not enought transactions referenced)");
            return false;
        }

        foreach (var txId in tx.Parents)
        {
            var parentTx = Repository.GetTimestamp(txId);

            if (parentTx is null)
            {
                Logger.LogInformation("AddTransaction rejected (reason = unknown transaction reference)");
                return false;
            }

            if (tx.Timestamp < parentTx)
            {
                Logger.LogInformation("AddTransaction rejected (reason = invalid timestamp)");
                return false;
            }
        }

        switch (tx.TransactionType)
        {
            case TransactionType.BLOCK:
                return AddBlock(new Block(tx, tx.Parents), broadcast);
            case TransactionType.PAYMENT:
            case TransactionType.CONTRACT:
                var payment = new Transaction(tx, tx.Parents);

                return AddTransaction(payment, broadcast, writeBatch);
            case TransactionType.VIEW:
                var view = new View(tx, tx.Parents);

                if (!tx.IsVerified && !view.Verify())
                {
                    Logger.LogInformation("AddTransaction rejected (reason = signature verification failed)");
                    return false;
                }

                if (!AddView(view, broadcast, broadcast))
                {
                    return false;
                }

                var vote = new Vote(Node.PublicKey, CurrentView.Parents.Take(1).ToList());

                vote.Parents.Add(CurrentView.TransactionId);
                vote.Sign(Node.PrivateKey);

                return AddVote(vote, true);
            case TransactionType.VOTE:
                var vote2 = new Vote(tx, tx.Parents);

                if (!vote2.Verify())
                {
                    Logger.LogInformation("AddTransaction rejected (reason = signature verification failed)");
                    return false;
                }

                return AddVote(vote2, broadcast);
            default:
                Logger.LogInformation($"Unknown transaction type ({tx.TransactionType})");
                return false;
        }
    }

    public bool AddVote(Vote vote, bool broadcast)
    {
        using var _ = rwlock.EnterWriteLockEx();
        //using var dbtx = Repository.BeginTransaction();

        var exists = Repository.Exists(vote.TransactionId);

        if (exists)
        {
            // we already have this
            return true;
        }

        Repository.Add(vote);

        PendingCache.Add(vote.TransactionId, vote);

        if (broadcast)
        {
            TransactionBuffer.Add(new TransactionDto(vote));
        }
        //dbtx.Commit();
        return true;
    }

    private void TraverseTransaction(Transaction transaction, long height, List<Transaction> toExecute, ref int voteCount, ref int blockCount)
    {
        transaction.Height = height;

        toExecute.Add(transaction);

        switch (transaction.TransactionType)
        {
            case TransactionType.VOTE:
                if (!Constant.SEED_VALIDATORS.Contains(transaction.PublicKey!))
                {
                    voteCount++;
                }
                break;
            case TransactionType.BLOCK:
                blockCount++;
                break;
        }

        foreach (var parentId in transaction.Parents)
        {
            if (!PendingCache.Remove(parentId, out var parent))
            {
                continue;
            }

            if (parent.Height is not null)
            {
                continue;
            }

            TraverseTransaction(parent, height, toExecute, ref voteCount, ref blockCount);
        }
    }

    public Genesis? GetGenesis()
    {
        using var _ = rwlock.EnterReadLockEx();
        return Repository.GetGenesis();
    }

    public List<Vote> GetVotesAtHeight(long height)
    {
        using var _ = rwlock.EnterReadLockEx();
        return Repository.GetVotesAtHeight(height);
    }

    public View? GetLastView()
    {
        using var _ = rwlock.EnterReadLockEx();
        return Repository.GetLastView();
    }

    public List<SHA256Hash> GetTransactionToValidate()
    {
        using var _ = rwlock.EnterReadLockEx();
        return Repository.GetTransactionsToValidate();
    }

    public void AddTransactionBatch(IEnumerable<TransactionDto> transactions)
    {
        Parallel.ForEach(transactions, tx => {
            if (!tx.Verify())
            {
                Logger.LogInformation("AddTransaction rejected (reason = invalid signature)");
                return;
            }

            tx.IsVerified = true;
        });

        using var _ = rwlock.EnterWriteLockEx();
        //using var dbtx = Repository.BeginTransaction();

        try
        {
            foreach (var tx in transactions)
            {
                if (!tx.IsVerified)
                {
                    continue;
                }

                AddTransaction(tx, true);
            }

            //dbtx.Commit();
        }
        catch (Exception ex)
        {
            //dbtx.Rollback();
            Logger.LogError(ex, "AddTransactionBatch error");
        }
    }

    /*public bool AddBlock(PosBlock block, bool broadcastBlock = true, bool broadcastVote = true)
    {
        using var _ = rwlock.EnterWriteLockEx();
        using var blockchainRepository = new BlockchainRepository();
        using var txCtx = blockchainRepository.Context.Database.BeginTransaction();

        try
        {
            var chainState = blockchainRepository.GetChainState();

            var blockchainContext = new BlockchainExContext()
            {
                LastBlocks = blockchainRepository.Tail(11),
                CurrentDifficulty = chainState.POW.CurrentDifficulty,
                NetworkTime = discoveryManager.GetNetworkTime()
            };

            var powExcecutor = Executor.Create<PowBlock, BlockchainExContext>(blockchainContext, logger)
                .Link<VerifyDifficulty>()
                .Link<VerifyNonce>()
                .Link<VerifyId>(x => x.Height > 0)
                .Link<VerifyParentHash>(x => x.Height > 0)
                .Link<VerifyTimestampPast>(x => x.Height > 0)
                .Link<VerifyTimestampFuture>(x => x.Height > 0);

            if (block.Pow != null && !powExcecutor.Execute(block.Pow, out var result))
            {
                logger.LogWarning($"AddBlock failed with: {result}");
                txCtx.Rollback();
                return false;
            }

            var wallets = walletManager.GetWallets();
            var seed = blockchainContext.LastBlocks.DefaultIfEmpty()
                .TakeLast(11)
                .Average(x => x?.Timestamp ?? 0);

            var txContext = new TransactionContext(blockchainRepository, wallets)
            {
                Height = block.Pow?.Height ?? 0,
                Fee = block.Pow?.Transactions.Where(x => x.TransactionType == TransactionType.PAYMENT).Select(x => x.MaxFee).DefaultIfEmpty().Min() ?? 0,
                FeeTotal = (ulong)(block.Pow?.Transactions.Where(x => x.TransactionType == TransactionType.PAYMENT).Select(x => (long)x.MaxFee).DefaultIfEmpty().Sum() ?? 0L),
                Timestamp = block.Timestamp,
                Seed = (int)seed
            };

            if (block.Pow is not null)
            {
                var txExecutor = Executor.Create<Transaction, TransactionContext>(txContext, logger)
                    // Miner fee
                    .Link<VerifyBlockReward>(x => x.TransactionType == TransactionType.MINER_FEE)
                    .Link<FetchRecipientWallet>(x => x.TransactionType == TransactionType.MINER_FEE)
                    .Link<AddBlockRewardToRecipient>(x => x.TransactionType == TransactionType.MINER_FEE)
                    .Link<UpdateRecipientWallet>(x => x.TransactionType == TransactionType.MINER_FEE)
                    // Payment, TODO: check for duplicate TX
                    .Link<VerifySignature>(x => x.TransactionType == TransactionType.PAYMENT || x.TransactionType == TransactionType.CONTRACT)
                    .Link<FetchSenderWallet>(x => x.TransactionType == TransactionType.PAYMENT && x.To.IsWallet())
                    .Link<TakeBalanceFromSender>(x => x.TransactionType == TransactionType.PAYMENT && x.To.IsWallet())
                    .Link<UpdateSenderWallet>(x => x.TransactionType == TransactionType.PAYMENT && x.To.IsWallet())
                    .Link<FetchRecipientWallet>(x => x.TransactionType == TransactionType.PAYMENT && x.To.IsWallet())
                    .Link<AddBalanceToRecipient>(x => x.TransactionType == TransactionType.PAYMENT && x.To.IsWallet())
                    .Link<UpdateRecipientWallet>(x => x.TransactionType == TransactionType.PAYMENT && x.To.IsWallet())
                    // Payment to Contract
                    .Link<FetchSenderWallet>(x => x.TransactionType == TransactionType.PAYMENT && x.To.IsContract())
                    .Link<TakeBalanceFromSender>(x => x.TransactionType == TransactionType.PAYMENT && x.To.IsContract())
                    .Link<UpdateSenderWallet>(x => x.TransactionType == TransactionType.PAYMENT && x.To.IsContract())
                    .Link<FetchContract>(x => x.TransactionType == TransactionType.PAYMENT && x.To.IsContract())
                    .Link<AddBalanceToContract>(x => x.TransactionType == TransactionType.PAYMENT && x.To.IsContract())
                    .Link<FetchOwnerWallet>(x => x.TransactionType == TransactionType.PAYMENT && x.To.IsContract())
                    .Link<ExecuteContract>(x => x.TransactionType == TransactionType.PAYMENT && x.To.IsContract())
                    // Add contract
                    .Link<AddContract>(x => x.TransactionType == TransactionType.CONTRACT);

                if (!txExecutor.ExecuteBatch(block.Pow.Transactions, out var res))
                {
                    logger.LogWarning($"AddBlock failed with: {res}");
                    txCtx.Rollback();
                    return false;
                }

                walletManager.UpdateWallets(txContext.Wallets.Where(x => x.Value != null).Select(x => x.Value).Where(x => x.Updated));
                blockchainRepository.UpdateWallets(txContext.LedgerWalletCache.Where(x => x.Value != null).Select(x => x.Value));
                blockchainRepository.UpdateContracts(txContext.ContractCache.Where(x => x.Value != null).Select(x => x.Value));

                if (block.Pow.Height % Constant.EPOCH_LENGTH_BLOCKS == 0)
                {
                    var epochStart = blockchainRepository.GetPowBlock(block.Pow.Height - Constant.EPOCH_LENGTH_BLOCKS + 1);
                    NextEpoch(epochStart, block.Pow, chainState);
                }

                chainState.POW.Height = block.Pow.Height;
                chainState.POW.TotalWork += block.Pow.Difficulty.ToWork();
                chainState.POW.LastHash = block.Pow.GetHash();
            }

            chainState.POS.Height = block.Height;
            chainState.POS.LastHash = block.GetHash();

            if (broadcastBlock)
            {
                BlockBroadcast.Post(block);
            }

            if (broadcastVote && block.Pow is not null)
            {
                var nodeWallet = walletManager.GetNodeWallet() ?? throw new Exception("Trying to sign vote without node keys");

                var vote = new Vote
                {
                    Height = block.Height,
                    Hash = block.GetHash(),
                    PublicKey = nodeWallet.PublicKey
                };

                vote.Sign(nodeWallet.PrivateKey);

                block.Votes.Add(vote);

                VoteBroadcast.Post(vote);
            }

            blockchainRepository.Add(block, chainState);
            txCtx.Commit();

            if (block.Pow is not null)
            {
                mempoolManager.RemoveTransactions(block.Pow.Transactions.Where(x => x.TransactionType == TransactionType.PAYMENT || x.TransactionType == TransactionType.CONTRACT));
            }

            logger.LogInformation($"Added block {block.Height}");

            ChainStateBroadcast.Post(chainState);

            foreach (var ev in txContext.Events)
            {
                // for some events we want to notify wallet
                switch (ev)
                {
                    case TransferTokenEventArgs transfer:
                        if (txContext.Wallets.ContainsKey(transfer.From.ToString()) || txContext.Wallets.ContainsKey(transfer.To.ToString()))
                        {
                            TokenTransferredBroadcast.Post(transfer);
                        }
                        break;
                    case ConsumeTokenEventArgs consume:
                        if (consume.Owner is not null && txContext.Wallets.ContainsKey(consume.Owner.ToString()))
                        {
                            TokenConsumedBroadcast.Post(consume);
                        }
                        break;
                }

                // TODO: Post events to websocket subscribers
            }

            foreach (var wallet in txContext.Wallets.Select(x => x.Value).Where(x => x.Updated))
            {
                WalletBroadcast.Post(wallet);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Add block failed");
            txCtx.Rollback();
            return false;
        }

        return true;
    }

    // Only use for chain sync!
    private bool AddBlocks(List<PosBlock> blocks)
    {
        int progress = 0;
        ChainObserver.ReportProgress("Inserting new blocks", progress, blocks.Count);

        using var blockchainRepository = new BlockchainRepository();

        var chainState = blockchainRepository.GetChainState();
        var epochStart = blockchainRepository.GetPowBlock(chainState.POW.Height - (chainState.POW.Height % Constant.EPOCH_LENGTH_BLOCKS) + 1);

        foreach (var chunk in blocks.Chunk(1000))
        {
            using var tx = blockchainRepository.Context.Database.BeginTransaction();

            try
            {
                var wallets = walletManager.GetWallets();

                var blockchainContext = new BlockchainExContext()
                {
                    LastBlocks = blockchainRepository.Tail(chunk.Select(x => x.Height).Min(), 11),
                    CurrentDifficulty = chainState.POW.CurrentDifficulty,
                    NetworkTime = discoveryManager.GetNetworkTime()
                };

                var powExcecutor = Executor.Create<PowBlock, BlockchainExContext>(blockchainContext, logger)
                    .Link<VerifyDifficulty>()
                    .Link<VerifyId>(x => x.Height > 0)
                    .Link<VerifyParentHash>(x => x.Height > 0)
                    .Link<VerifyTimestampPast>(x => x.Height > 0)
                    .Link<VerifyTimestampFuture>(x => x.Height > 0);

                var txContext = new TransactionContext(blockchainRepository, wallets);

                // TODO: This needs refactoring
                var txExecutor = Executor.Create<Transaction, TransactionContext>(txContext, logger)
                    // Miner fee
                    .Link<VerifyBlockReward>(x => x.TransactionType == TransactionType.MINER_FEE)
                    .Link<FetchRecipientWallet>(x => x.TransactionType == TransactionType.MINER_FEE)
                    .Link<AddBlockRewardToRecipient>(x => x.TransactionType == TransactionType.MINER_FEE)
                    .Link<UpdateRecipientWallet>(x => x.TransactionType == TransactionType.MINER_FEE)
                    // Payment, TODO: check for duplicate TX
                    .Link<VerifySignature>(x => x.TransactionType == TransactionType.PAYMENT || x.TransactionType == TransactionType.CONTRACT)
                    .Link<FetchSenderWallet>(x => x.TransactionType == TransactionType.PAYMENT && x.To.IsWallet())
                    .Link<TakeBalanceFromSender>(x => x.TransactionType == TransactionType.PAYMENT && x.To.IsWallet())
                    .Link<UpdateSenderWallet>(x => x.TransactionType == TransactionType.PAYMENT && x.To.IsWallet())
                    .Link<FetchRecipientWallet>(x => x.TransactionType == TransactionType.PAYMENT && x.To.IsWallet())
                    .Link<AddBalanceToRecipient>(x => x.TransactionType == TransactionType.PAYMENT && x.To.IsWallet())
                    .Link<UpdateRecipientWallet>(x => x.TransactionType == TransactionType.PAYMENT && x.To.IsWallet())
                    // Payment to Contract
                    .Link<FetchSenderWallet>(x => x.TransactionType == TransactionType.PAYMENT && x.To.IsContract())
                    .Link<TakeBalanceFromSender>(x => x.TransactionType == TransactionType.PAYMENT && x.To.IsContract())
                    .Link<UpdateSenderWallet>(x => x.TransactionType == TransactionType.PAYMENT && x.To.IsContract())
                    .Link<FetchContract>(x => x.TransactionType == TransactionType.PAYMENT && x.To.IsContract())
                    .Link<AddBalanceToContract>(x => x.TransactionType == TransactionType.PAYMENT && x.To.IsContract())
                    .Link<FetchOwnerWallet>(x => x.TransactionType == TransactionType.PAYMENT && x.To.IsContract())
                    .Link<ExecuteContract>(x => x.TransactionType == TransactionType.PAYMENT && x.To.IsContract())
                    // Add contract
                    .Link<AddContract>(x => x.TransactionType == TransactionType.CONTRACT && x.To.IsContract());

                foreach (var block in chunk)
                {
                    var seed = blockchainContext.LastBlocks.TakeLast(11).Average(x => x.Timestamp);

                    if (block.Pow != null)
                    {
                        if (!powExcecutor.Execute(block.Pow, out var result))
                        {
                            logger.LogWarning($"AddBlock ({block.Pow.Height}) failed with: {result}");
                            return false;
                        }

                        txContext.Height = block.Pow.Height;
                        txContext.Fee = block.Pow.Transactions.Where(x => x.TransactionType == TransactionType.PAYMENT).Select(x => x.MaxFee).DefaultIfEmpty().Min();
                        txContext.FeeTotal = (ulong)block.Pow.Transactions.Where(x => x.TransactionType == TransactionType.PAYMENT).Select(x => (long)x.MaxFee).DefaultIfEmpty().Sum();
                        txContext.Timestamp = block.Pow.Timestamp;
                        txContext.Seed = (int)seed;

                        if (!txExecutor.ExecuteBatch(block.Pow.Transactions, out var res))
                        {
                            logger.LogWarning($"AddBlock transactions failed with: {res}");
                            return false;
                        }

                        if (block.Height % Constant.EPOCH_LENGTH_BLOCKS == 0)
                        {
                            NextEpoch(epochStart, block.Pow, chainState);
                            blockchainContext.CurrentDifficulty = chainState.POW.CurrentDifficulty;
                        }

                        if (block.Height % Constant.EPOCH_LENGTH_BLOCKS == 1)
                        {
                            epochStart = block.Pow;
                        }

                        chainState.POW.Height = block.Pow.Height;
                        chainState.POW.TotalWork += block.Pow.Difficulty.ToWork();
                        chainState.POW.LastHash = block.Pow.GetHash();

                        blockchainContext.LastBlocks.Add(block.Pow);
                    }

                    chainState.POS.Height = block.Height;
                    chainState.POS.LastHash = block.GetHash();

                    logger.LogInformation($"Added block {block.Height}");

                    ChainObserver.ReportProgress("Inserting new blocks", ++progress, blocks.Count);
                }

                walletManager.UpdateWallets(txContext.Wallets.Select(x => x.Value).Where(x => x.Updated));

                blockchainRepository.UpdateWallets(txContext.LedgerWalletCache.Select(x => x.Value));
                blockchainRepository.UpdateContracts(txContext.ContractCache.Select(x => x.Value));
                blockchainRepository.UpdateTokens(txContext.TokenCache.Select(x => x.Value));
                blockchainRepository.Add(chunk, chainState);

                ChainStateBroadcast.Post(chainState);

                foreach (var wallet in txContext.Wallets.Select(x => x.Value).Where(x => x.Updated))
                {
                    WalletBroadcast.Post(wallet);
                }

                foreach (var ev in txContext.Events)
                {
                    // for some events we want to notify wallet
                    switch (ev)
                    {
                        case TransferTokenEventArgs transfer:
                            if (txContext.Wallets.ContainsKey(transfer.From.ToString()) || txContext.Wallets.ContainsKey(transfer.To.ToString()))
                            {
                                TokenTransferredBroadcast.Post(transfer);
                            }
                            break;
                        case ConsumeTokenEventArgs consume:
                            if (txContext.Wallets.ContainsKey(consume.Owner.ToString()))
                            {
                                TokenConsumedBroadcast.Post(consume);
                            }
                            break;
                    }

                    // TODO: Post events to websocket subscribers
                }

                tx.Commit();
            }
            catch (Exception ex)
            {
                tx.Rollback();
                throw new Exception("Chain rollback failed", ex);
            }
        }

        return true;
    }
    */

    public Blocktemplate GetBlocktemplate(Address wallet)
    {
        using var _ = rwlock.EnterReadLockEx();
        //using var dbtx = Repository.BeginTransaction();

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var chainState = Repository.GetChainState();
        var block = new Block(wallet, timestamp, chainState.LastHash, chainState.CurrentDifficulty, Repository.GetTransactionsToValidate());

        return new Blocktemplate
        {
            Height = chainState.Height,
            To = wallet,
            Difficulty = chainState.CurrentDifficulty,
            ParentHash = block.ParentHash,
            Nonce = block.GetHash(),
            Timestamp = block.Timestamp,
            Validates = block.Parents,
            Data = block.Data ?? Array.Empty<byte>()
        };
    }

    public ChainState GetChainState()
    {
        using var _ = rwlock.EnterReadLockEx();
        return Repository.GetChainState()!;
    }

    public Difficulty GetCurrentDifficulty()
    {
        using var _ = rwlock.EnterReadLockEx();

        var chainState = Repository.GetChainState();
        return chainState!.CurrentDifficulty;
    }

    public long GetCurrentHeight()
    {
        using var _ = rwlock.EnterReadLockEx();

        var chainState = Repository.GetChainState();
        return chainState.Height;
    }

    public List<Transaction> GetLastNTransctions(Address address, int count)
    {
        using var _ = rwlock.EnterReadLockEx();
        return Repository.GetLastNTransctions(address, count);
    }

    public long GetBalance(Address address)
    {
        using var _ = rwlock.EnterReadLockEx();

        if (address.IsContract())
        {
            return Repository.GetContract(address)?.Balance ?? 0;
        }

        return Repository.GetWallet(address)?.Balance ?? 0;
    }

    /*
    public bool SetChain(List<PosBlock> blocks)
    {
        using var _ = rwlock.EnterWriteLockEx();

        try
        {
            var sortedBlocks = blocks.Where(x => x.Height > 0)
                .OrderBy(x => x.Height)
                .ToList();

            RollbackChainIfNeeded(sortedBlocks);

            if(!AddBlocks(sortedBlocks))
            {
                logger.LogError($"Set chain failed");
                return false;
            }

            var wallets = walletManager.GetWallets();
            foreach (var wallet in wallets.Select(x => x.Value))
            {
                WalletBroadcast.Post(wallet);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Chain reorg failure");
            return false;
        }

        logger.LogInformation("Chain synchronization completed");
        return true;
    }

    private void RollbackChainIfNeeded(List<PosBlock> sortedBlocks)
    {
        using var blockchainRepository = new BlockchainRepository();
        using var txContext = blockchainRepository.Context.Database.BeginTransaction();

        try
        {
            long progress = 0;

            var chainState = blockchainRepository.GetChainState();
            var ledgerWallets = new Dictionary<string, LedgerWallet>();
            var wallets = walletManager.GetWallets();

            var min = sortedBlocks.First().Height;
            var max = chainState.POS.Height;

            if (min > max)
            {
                return;
            }

            ChainObserver.ReportProgress("Rolling back current chain", progress, sortedBlocks.Count);

            for (long i = max; i >= min; i--)
            {
                var cBlock = blockchainRepository.GetPosBlock(i);

                if (cBlock == null)
                {
                    continue;
                }

                if (cBlock.Pow is not null)
                {
                    var powFee = cBlock.Pow.Transactions
                        .Where(tx => tx.TransactionType == TransactionType.PAYMENT || tx.TransactionType == TransactionType.CONTRACT)
                        .DefaultIfEmpty()
                        .Select(x => x?.MaxFee ?? 0UL).Min();

                    foreach (var tx in cBlock.Pow.Transactions)
                    {
                        if (tx.PublicKey != null)
                        {
                            var senderAddr = tx.PublicKey.ToAddress();
                            if (!ledgerWallets.ContainsKey(senderAddr.ToString()))
                            {
                                ledgerWallets.Add(senderAddr.ToString(), blockchainRepository.GetWallet(senderAddr));
                            }

                            var sender = ledgerWallets[senderAddr.ToString()];

                            checked
                            {
                                sender.Balance += tx.Value;
                                sender.Balance += powFee;
                            }

                            if (wallets.TryGetValue(senderAddr.ToString(), out var sWallet))
                            {
                                sWallet.Balance = sender.Balance;
                                sWallet.WalletTransactions.RemoveAll(x => x.Height == cBlock.Height);
                                sWallet.Updated = true;
                            }
                        }

                        if (tx.To.IsContract())
                        {
                            var contract = blockchainRepository.GetContract(tx.To, true);

                            if (contract is not null)
                            {
                                foreach (var effect in tx.Effects)
                                {
                                    if (effect.IsTokenEffect())
                                    {
                                        RollbackTokenEffect(blockchainRepository, ledgerWallets, contract, effect);
                                    }
                                    else
                                    {
                                        RollbackEffectBalance(blockchainRepository, ledgerWallets, contract, effect);
                                    }
                                }
                            }
                        }
                        else
                        {
                            var recipientAddr = tx.To.ToString();
                            if (!ledgerWallets.ContainsKey(recipientAddr))
                            {
                                ledgerWallets.Add(recipientAddr, blockchainRepository.GetWallet(tx.To));
                            }

                            var recipient = ledgerWallets[recipientAddr];

                            recipient.Balance = checked(recipient.Balance - tx.Value);

                            if (tx.TransactionType == TransactionType.MINER_FEE)
                            {
                                var totalFee = powFee * (ulong)(cBlock.Pow.Transactions.Count - 1);
                                recipient.Balance = checked(recipient.Balance - totalFee);
                            }

                            if (wallets.TryGetValue(tx.To.ToString(), out var rWallet))
                            {
                                rWallet.Balance = recipient.Balance;
                                rWallet.WalletTransactions.RemoveAll(x => x.Height == cBlock.Height);
                                rWallet.Updated = true;
                            }
                        }
                    }

                    chainState.POW.TotalWork -= cBlock.Pow!.Difficulty.ToWork();
                }

                ChainObserver.ReportProgress("Rolling back current chain", ++progress, sortedBlocks.Count);
            }

            var currentTip = blockchainRepository.GetPosBlock(min - 1);

            chainState.POW.Height = min - 1;
            chainState.POW.CurrentDifficulty = currentTip.Pow.Difficulty;
            chainState.POW.LastHash = currentTip.Pow?.GetHash() ?? new SHA256Hash();
            chainState.POS.Height = min - 1;
            chainState.POS.LastHash = currentTip.GetHash() ?? new SHA256Hash();


            if (wallets.Values.Count > 0)
            {
                walletManager.RollbackWallets(wallets.Values.ToList(), min);
            }

            blockchainRepository.UpdateWallets(ledgerWallets.Values);
            blockchainRepository.Delete(chainState.POS.Height);
            blockchainRepository.DeleteContractSnapshot(chainState.POS.Height);
            blockchainRepository.SaveState(chainState);

            txContext.Commit();
        }
        catch (Exception ex)
        {
            txContext.Rollback();
            throw new Exception("Chain rollback failed", ex);
        }
    }
    */

    public void ResetChain()
    {
        using var _ = rwlock.EnterWriteLockEx();

        // Repository.GetContext().Database.EnsureDeleted();
        // Repository.GetContext().Database.Migrate();
    }

    public Contract? GetContract(Address address)
    {
        using var _ = rwlock.EnterReadLockEx();

        return Repository.GetContract(address);
    }

    public List<Ledger> GetRichList(int count)
    {
        using var _ = rwlock.EnterReadLockEx();

        return Repository.GetRichList(count);
    }
    public List<Transaction> GetTransactionsForAddress(Address address)
    {
        using var _ = rwlock.EnterReadLockEx();

        return Repository.GetTransactions(address);
    }

    public Transaction? GetTransactionForHash(SHA256Hash hash)
    {
        using var _ = rwlock.EnterReadLockEx();

        return Repository.Get(hash);
    }

    public Ledger? GetLedger(Address address)
    {
        using var _ = rwlock.EnterReadLockEx();

        return Repository.GetWallet(address);
    }
    
    public string? CallContractMethod(Address address, CallMethod call)
    {
        using var _ = rwlock.EnterReadLockEx();

        var contract = Repository.GetContract(address) ?? throw new Exception(ExecutionResult.INVALID_CONTRACT.ToString());

        var snapshot = contract.Snapshots
            .OrderByDescending(x => x.Height)
            .FirstOrDefault();

        if (snapshot == null)
        {
            throw new Exception(ExecutionResult.CONTRACT_SNAPSHOT_MISSING.ToString());
        }

        var methodName = $"{call.Method}";
        var method = contract.Manifest.Methods
            .Where(x => x.Name == methodName)
            .FirstOrDefault();

        if (method == null)
        {
            throw new Exception(ExecutionResult.INVALID_METHOD.ToString());
        }

        if (!method.IsReadonly)
        {
            throw new Exception("only readonly methods can be called without transaction");
        }

        var methodParams = new List<object> { contract.EntryPoint ?? throw new Exception(ExecutionResult.CONTRACT_ENTRYPOINT_MISSING.ToString()) };

        if (call.Params is not null)
        {
            methodParams.AddRange(call.Params);
        }

        var vmContext = new VMContext(contract, new Transaction { To = address }, Random.Shared, Logger);

        using var vm = KryoVM.LoadFromSnapshot(contract.Code, snapshot.Snapshot)
            .WithContext(vmContext);

        Console.WriteLine($"Executing contract {contract.Name}:{call.Method}");
        var ret = vm.CallMethod(methodName, methodParams.ToArray(), out var json);
        Console.WriteLine($"Contract result = {ret}");

        return json;
    }

    public Token? GetToken(SHA256Hash tokenId)
    {
        using var _ = rwlock.EnterReadLockEx();

        return Repository.GetToken(tokenId);
    }

    public List<Token> GetTokens(Address address)
    {
        using var _ = rwlock.EnterReadLockEx();

        return Repository.GetTokens(address);
    }

    public List<Token> GetContractTokens(Address contractAddress)
    {
        using var _ = rwlock.EnterReadLockEx();

        return Repository.GetContractTokens(contractAddress);
    }

    private void RollbackEffectBalance(StoreRepository repository, Dictionary<string, Ledger> walletCache, Contract contract, Effect effect)
    {
        if (!walletCache.ContainsKey(effect.To.ToString()))
        {
            walletCache.Add(effect.To.ToString(), repository.GetWallet(effect.To));
        }

        var toWallet = walletCache[effect.To.ToString()];

        checked
        {
            toWallet.Balance -= effect.Value;
            contract.Balance += effect.Value;
        }
    }


    private void RollbackTokenEffect(StoreRepository repository, Dictionary<string, Ledger> walletCache, Contract contract, Effect effect)
    {
        if (effect.TokenId is null)
        {
            throw new ArgumentNullException("effect.TokenId is null, unable to rollback token");
        }

        if (!walletCache.ContainsKey(effect.From.ToString()))
        {
            walletCache.Add(effect.From.ToString(), repository.GetWallet(effect.From));
        }

        var fromWallet = walletCache[effect.From.ToString()];

        var token = repository.GetToken(effect.TokenId);

        if (token is null)
        {
            Logger.LogWarning($"Trying to rollback nonexisting token: {effect.TokenId}");
            return;
        }

        if (effect.ConsumeToken)
        {
            token.IsConsumed = false;
            return;
        }

        if (effect.From == contract.Address)
        {
            // repository.Context.Remove<Token>(token);
        }
        else
        {
            token.Ledger = fromWallet.Address;
        }
    }

    public View? GetView(SHA256Hash transactionId)
    {
        throw new NotImplementedException();
    }

    public List<Transaction> GetTransactionsAfterHeight(long height, int batchSize)
    {
        throw new NotImplementedException();
    }
}
