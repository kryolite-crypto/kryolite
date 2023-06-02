using System;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Kryolite.Node.Executor;
using Kryolite.Node.Repository;
using Kryolite.Node.Services;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using Kryolite.Shared.Dto;
using MessagePack;
using Microsoft.Extensions.Logging;
using Redbus.Interfaces;

namespace Kryolite.Node;

public class BlockchainManager : IBlockchainManager
{
    private IBlockchainRepository Repository { get; }
    private IBufferService<TransactionDto> TransactionBuffer { get; }
    private IExecutorFactory ExecutorFactory { get; }
    private INetworkManager NetworkManager { get; }
    private IWalletManager WalletManager { get; }
    private IEventBus EventBus { get; }
    private ILogger<BlockchainManager> Logger { get; }

    private static ReaderWriterLockSlim rwlock = new(LockRecursionPolicy.SupportsRecursion);

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
    private static Dictionary<SHA256Hash, Transaction> PendingCache;
    private static Dictionary<Address, Ledger> WalletCache;
    private static View CurrentView;
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

    public BlockchainManager(IBlockchainRepository repository, IBufferService<TransactionDto> transactionBuffer, IExecutorFactory executorFactory, INetworkManager networkManager, IWalletManager walletManager, IEventBus eventBus, ILogger<BlockchainManager> logger)
    {
        Repository = repository ?? throw new ArgumentNullException(nameof(repository));
        TransactionBuffer = transactionBuffer ?? throw new ArgumentNullException(nameof(transactionBuffer));
        ExecutorFactory = executorFactory ?? throw new ArgumentNullException(nameof(executorFactory));
        NetworkManager = networkManager ?? throw new ArgumentNullException(nameof(networkManager));
        WalletManager = walletManager ?? throw new ArgumentNullException(nameof(walletManager));
        EventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));

        using var _ = rwlock.EnterWriteLockEx();

        if (PendingCache is null)
        {
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

        if (WalletCache is null)
        {
            WalletCache = new();
        }

        if (CurrentView is null)
        {
            CurrentView = Repository.GetLastView()!;
        }
    }

    public bool AddGenesis(Genesis genesis)
    {
        using var _ = rwlock.EnterWriteLockEx();

        try
        {
            genesis.TransactionId = genesis.CalculateHash();

            Repository.Add(genesis);

            var chainState = new ChainState
            {
                Height = -1,
                LastHash = genesis.TransactionId,
                CurrentDifficulty = BigInteger.Pow(new BigInteger(2), Constant.STARTING_DIFFICULTY).ToDifficulty()
            };

            Repository.CreateState(chainState);

            PendingCache.Add(genesis.TransactionId, genesis);

            return true;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "AddGenesis error");
        }

        return false;
    }

    public bool AddView(View view, bool broadcast)
    {
        using var _ = rwlock.EnterWriteLockEx();
        var dbTx = Repository.BeginTransaction();

        try
        {
            var sw = Stopwatch.StartNew();

            var height = view.Height ?? 0;

            view.TransactionId = view.CalculateHash();

            var chainState = Repository.GetChainState();

            if (height != chainState.Height + 1)
            {
                Logger.LogInformation("Discarding view #{height} (reason = invalid height)", view.Height);
                return false;
            }

            var toExecute = new Dictionary<SHA256Hash, Transaction>();

            if (height > 0)
            {
                var earliest = CurrentView.Timestamp + Constant.HEARTBEAT_INTERVAL;

                if (view.Timestamp < earliest)
                {
                    Logger.LogInformation("Discarding view #{height} (reason = timestamp too early)", view.Height);
                    return false;
                }

                toExecute.Add(CurrentView.TransactionId, CurrentView);
            }

            Repository.Add(view);

            foreach (var parentId in view.Parents)
            {
                if (!PendingCache.TryGetValue(parentId, out var parent))
                {
                    continue;
                }

                if (parent.Height is not null)
                {
                    continue;
                }

                TraverseTransaction(parent, height, toExecute);
            }

            var context = new ExecutorContext(Repository, WalletCache);
            var executor = ExecutorFactory.Create(context);

            executor.Execute(toExecute.Values.ToList(), out var blockCount);

            Repository.UpdateStatus(toExecute.Values.ToList());

            if (height > 0)
            {
                if (blockCount == 0)
                {
                    var work = chainState.CurrentDifficulty.ToWork();
                    var nextTarget = work / 4 * 3;
                    var minTarget = BigInteger.Pow(new BigInteger(2), Constant.STARTING_DIFFICULTY);

                    chainState.CurrentDifficulty = BigInteger.Max(minTarget, nextTarget).ToDifficulty();
                }
                else
                {
                    var totalWork = chainState.CurrentDifficulty.ToWork() * blockCount;

                    chainState.Weight += totalWork;
                    chainState.CurrentDifficulty = totalWork.ToDifficulty();
                }
            }

            Logger.LogInformation($"Next difficulty {chainState.CurrentDifficulty}");

            chainState.Height++;
            chainState.LastHash = view.TransactionId;
            chainState.Weight += chainState.CurrentDifficulty.ToWork() * toExecute.Count;
            Repository.SaveState(chainState);

            dbTx.Commit();

            Logger.LogInformation($"Finalized {toExecute.Count} transactions in {sw.Elapsed.TotalNanoseconds / 1000000}ms");

            EventBus.PublishAsync(chainState);

            var wallets = WalletManager.GetWallets();
            var addresses = new List<string>();

            foreach (var transaction in toExecute.Values)
            {
                if (transaction.PublicKey is not null)
                {
                    addresses.Add(transaction.PublicKey.ToAddress().ToString());
                }

                if (transaction.To is not null)
                {
                    addresses.Add(transaction.To.ToString());
                }
            }

            foreach (var address in addresses.Distinct())
            {
                var ledgerWallet = context.GetWallet(address);

                if (ledgerWallet is null)
                {
                    // should not be null since we have already gone thru every transaction
                    continue;
                }

                if (wallets.TryGetValue(address, out var wallet))
                {
                    wallet.Balance = ledgerWallet.Balance;
                    wallet.Pending = ledgerWallet.Pending;

                    EventBus.PublishAsync(wallet);
                }
            }

            WalletManager.UpdateWallets(wallets.Values);

            if (broadcast)
            {
                TransactionBuffer.Add(new TransactionDto(view));
            }

            foreach (var tx in toExecute)
            {
                PendingCache.Remove(tx.Key);
            }

            WalletCache.Clear();

            CurrentView = view;

            sw.Stop();
            Logger.LogInformation($"Added view #{height} in {sw.Elapsed.TotalNanoseconds / 1000000}ms");

            return true;
        }
        catch (Exception ex)
        {
            dbTx.Rollback();
            Logger.LogError(ex, "AddView error");
        }

        return false;
    }

    public bool AddBlock(Block block, bool broadcast)
    {
        using var _ = rwlock.EnterWriteLockEx();

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

        if (block.Parents.Count < 2)
        {
            Logger.LogInformation("AddBlock rejected (reason = not enought transactions referenced)");
            return false;
        }

        block.TransactionId = block.CalculateHash();

        var chainState = Repository.GetChainState();

        if (block.Difficulty != chainState.CurrentDifficulty)
        {
            Logger.LogInformation("AddBlock rejected (reason = invalid difficulty)");
            return false;
        }

        if (block.ParentHash != chainState.LastHash)
        {
            Logger.LogInformation("AddBlock rejected (reason = invalid parent hash)");
            return false;
        }

        var exists = Repository.Exists(block.TransactionId);

        if (exists)
        {
            Logger.LogInformation("AddBlock rejected (reason = already exists)");
            return false;
        }

        if (!block.VerifyNonce())
        {
            Logger.LogInformation("AddBlock rejected (reason = invalid nonce)");
            return false;
        }

        if (!WalletCache.TryGetValue(block.To, out var to))
        {
            to = Repository.GetWallet(block.To);

            if (to is null)
            {
                to = new Ledger(block.To);
            }

            WalletCache.Add(block.To, to);
        }

        checked
        {
            to.Pending += block.Value;
        }

        var wallet = WalletManager.GetWallet(to.Address.ToString());

        if (wallet is not null)
        {
            wallet.Pending = to.Pending;
            wallet.Balance = to.Balance;

            WalletManager.UpdateWallet(wallet);

            EventBus.PublishAsync(wallet);
        }

        chainState.Blocks++;

        Repository.UpdateWallet(to);
        Repository.Add(block);
        Repository.SaveState(chainState);

        sw.Stop();

        Logger.LogInformation($"Added block #{chainState.Blocks} in {sw.Elapsed.TotalNanoseconds / 1000000}ms [diff = {block.Difficulty}]");

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

        try
        {
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

            return AddBlock(block, broadcast);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "AddBlock error");
        }

        return false;
    }

    public bool AddTransaction(Transaction tx, bool broadcast)
    {
        using var _ = rwlock.EnterWriteLockEx();

        try
        {
            var sw = Stopwatch.StartNew();

            if (tx.TransactionType != TransactionType.PAYMENT && tx.TransactionType != TransactionType.BLOCK)
            {
                Logger.LogInformation("AddTransaction rejected (reason = wrong transaction type)");
                return false;
            }

            if (tx.TransactionType == TransactionType.PAYMENT && tx.Value == 0)
            {
                Logger.LogInformation("AddTransaction rejected (reason = zero payment)");
                return false;
            }

            if (tx.Parents.Count < 2)
            {
                Logger.LogInformation("AddTransaction rejected (reason = not enought transactions referenced)");
                return false;
            }

            if (tx.To is null)
            {
                Logger.LogInformation("AddTransaction rejected (reason = null 'to' address)");
                return false;
            }

            if (!tx.Verify())
            {
                Logger.LogInformation("AddTransaction rejected (reason = signature verification failed)");
                return false;
            }

            tx.TransactionId = tx.CalculateHash();

            var exists = Repository.Exists(tx.TransactionId);

            if (exists)
            {
                // no need to do anything, we have this already
                return true;
            }

            if (!WalletCache.TryGetValue(tx.From, out var from))
            {
                from = Repository.GetWallet(tx.From);

                if (from is null)
                {
                    from = new Ledger(tx.From);
                }

                WalletCache.Add(tx.From, from);
            }

            if (from is null)
            {
                Logger.LogInformation("AddTransaction rejected (reason = wallet not indexed)");
                return false;
            }

            if (from.Balance < tx.Value)
            {
                Logger.LogInformation("AddTransaction rejected (reason = too low balance)");
                return false;
            }

            if (!WalletCache.TryGetValue(tx.To, out var to))
            {
                to = Repository.GetWallet(tx.To);

                if (to is null)
                {
                    to = new Ledger(tx.From);
                }

                WalletCache.Add(tx.To, to);
            }

            checked
            {
                from.Balance -= tx.Value;
                to.Pending += tx.Value;
            }

            var fromWallet = WalletManager.GetWallet(from.Address.ToString());

            if (fromWallet is not null)
            {
                fromWallet.Balance = from.Balance;
                fromWallet.Pending = from.Pending;

                WalletManager.UpdateWallet(fromWallet);

                EventBus.PublishAsync(fromWallet);
            }

            var toWallet = WalletManager.GetWallet(to.Address.ToString());

            if (toWallet is not null)
            {
                toWallet.Balance = to.Balance;
                toWallet.Pending = to.Pending;

                WalletManager.UpdateWallet(toWallet);

                EventBus.PublishAsync(toWallet);
            }

            Repository.Add(tx);
            Repository.UpdateWallets(from, to);

            sw.Stop();

            Logger.LogInformation($"Added transaction in {sw.Elapsed.TotalNanoseconds / 1000000}ms [type = {tx.TransactionType}, value = {tx.Value}]");

            if (broadcast)
            {
                TransactionBuffer.Add(new TransactionDto(tx));
            }

            PendingCache.Add(tx.TransactionId, tx);

            return true;
        }
        catch (Exception ex) 
        {
            Logger.LogError(ex, "AddTransaction error");
        }

        return false;
    }

    public bool AddTransaction(TransactionDto tx, bool broadcast)
    {
        using var _ = rwlock.EnterWriteLockEx();

        foreach (var txId in tx.Parents)
        {
            var parentTx = Repository.Get(txId);

            if (parentTx is null)
            {
                Logger.LogInformation("AddTransaction rejected (reason = unknown transaction reference)");
                return false;
            }

            if (tx.Timestamp < parentTx.Timestamp)
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
                return AddTransaction(new Transaction(tx, tx.Parents), broadcast);
            case TransactionType.VIEW:
                return AddView(new View(tx, tx.Parents), broadcast);
            case TransactionType.VOTE:
                return AddVote(new Vote(tx, tx.Parents), broadcast);
            default:
                Logger.LogInformation($"Unknown transaction type ({tx.TransactionType})");
                return false;
        }
    }

    public bool AddVote(Vote vote, bool broadcast)
    {
        if (vote.Signature is null)
        {
            Logger.LogInformation("AddVote rejected (reason = null signature)");
            return false;
        }

        if (!vote.Verify())
        {
            Logger.LogInformation("AddVote rejected (reason = invalid signature)");
            return false;
        }

        using var _ = rwlock.EnterWriteLockEx();

        var chainState = Repository.GetChainState();

        bool referencesView = false;
        foreach (var parent in vote.Parents)
        {
            var tx = Repository.Get(parent);

            if (tx is null)
            {
                Logger.LogInformation("AddVote rejected (reason = references unknown tx)");
                return false;
            }

            if (tx.TransactionType != TransactionType.VOTE)
            {
                continue;
            }

            if (tx.Height < chainState.Height)
            {
                Logger.LogInformation("AddVote rejected (reason = references finalized view)");
                return false;
            }

            referencesView = true;
        }

        if (!referencesView)
        {
            Logger.LogInformation("AddVote rejected (reason = vote does not reference view)");
            return false;
        }

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

        return true;
    }

    private void TraverseTransaction(Transaction transaction, long height, Dictionary<SHA256Hash, Transaction> toExecute)
    {
        transaction.Height = height;

        toExecute[transaction.TransactionId] = transaction;

        foreach (var parentId in transaction.Parents)
        {
            if (!PendingCache.TryGetValue(parentId, out var parent))
            {
                continue;
            }

            if (parent.Height is not null)
            {
                continue;
            }

            TraverseTransaction(parent, height, toExecute);
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

    public void AddTransactionBatch(IList<TransactionDto> transactions)
    {
        using var _ = rwlock.EnterWriteLockEx();
        //using var dbTx = Repository.GetContext().Database.BeginTransaction();

        try
        {
            var valid = new List<TransactionDto>();

            foreach (var tx in transactions)
            {
                var validates = new List<Transaction>();
                
                if (AddTransaction(tx, false))
                {
                    valid.Add(tx);
                }
            }

            //dbTx.Commit();

            TransactionBuffer.Add(valid);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "AddTransactionBatch error");
            //dbTx.Rollback();
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
        return Repository.GetChainState();
    }

    public Difficulty GetCurrentDifficulty()
    {
        using var _ = rwlock.EnterReadLockEx();

        var chainState = Repository.GetChainState();
        return chainState.CurrentDifficulty;
    }

    public long GetCurrentHeight()
    {
        using var _ = rwlock.EnterReadLockEx();

        var chainState = Repository.GetChainState();
        return chainState.Height;
    }

    public ulong GetBalance(Address address)
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

        using var walletRepository = new WalletRepository();
        var wallets = walletRepository.GetWallets()
            .Select(x => x.Value)
            .ToList();

        walletRepository.RollbackWallets(wallets, 0);

        foreach (var wallet in wallets)
        {
            wallet.Balance = 0;
            walletRepository.Update(wallet);
        }
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

    public Ledger? GetLedgerWallet(Address address)
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

    private void RollbackEffectBalance(BlockchainRepository repository, Dictionary<string, Ledger> walletCache, Contract contract, Effect effect)
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

    private void RollbackTokenEffect(BlockchainRepository repository, Dictionary<string, Ledger> walletCache, Contract contract, Effect effect)
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
