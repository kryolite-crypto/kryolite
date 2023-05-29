using System;
using System.Diagnostics;
using System.Numerics;
using System.Threading.Tasks.Dataflow;
using Kryolite.Node.Executor;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using Kryolite.Shared.Dto;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kryolite.Node;

public class BlockchainManager : IBlockchainManager
{
    private IExecutorFactory ExecutorFactory { get; }
    private INetworkManager NetworkManager { get; }
    private IWalletManager WalletManager { get; }
    private ILogger<BlockchainManager> Logger { get; }

    private ReaderWriterLockSlim rwlock = new(LockRecursionPolicy.SupportsRecursion);

    private BroadcastBlock<TransactionDto> TransactionBroadcast = new(i => i);

    private BroadcastBlock<Vote> VoteBroadcast = new(i => i);
    private BroadcastBlock<ChainState> ChainStateBroadcast = new(i => i);
    private BroadcastBlock<Wallet> WalletBroadcast = new(i => i);
    private BroadcastBlock<TransferTokenEventArgs> TokenTransferredBroadcast = new(i => i);
    private BroadcastBlock<ConsumeTokenEventArgs> TokenConsumedBroadcast = new(i => i);

    public BlockchainManager(IExecutorFactory executorFactory, INetworkManager networkManager, IWalletManager walletManager, ILogger<BlockchainManager> logger)
    {
        ExecutorFactory = executorFactory ?? throw new ArgumentNullException(nameof(executorFactory));
        NetworkManager = networkManager ?? throw new ArgumentNullException(nameof(networkManager));
        WalletManager = walletManager ?? throw new ArgumentNullException(nameof(walletManager));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool AddGenesis(Genesis genesis)
    {
        using var _ = rwlock.EnterWriteLockEx();
        using var blockchainRepository = new BlockchainRepository();
        using var tx = blockchainRepository.Context.Database.BeginTransaction();

        try
        {
            genesis.TransactionId = genesis.CalculateHash();

            blockchainRepository.Add(genesis);

            var chainState = new ChainState
            {
                Id = 0,
                Height = -1,
                LastHash = genesis.TransactionId,
                CurrentDifficulty = BigInteger.Pow(new BigInteger(2), Constant.STARTING_DIFFICULTY).ToDifficulty()
            };

            blockchainRepository.SaveState(chainState);
            blockchainRepository.Context.SaveChanges();

            tx.Commit();

            return true;
        }
        catch (Exception ex)
        {
            tx.Rollback();
            Logger.LogError(ex, "AddGenesis error");
        }

        return false;
    }

    public bool AddView(View view, bool broadcast)
    {
        using var _ = rwlock.EnterWriteLockEx();
        using var blockchainRepository = new BlockchainRepository();
        using var tx = blockchainRepository.Context.Database.BeginTransaction();

        try
        {
            var height = view.Height ?? 0;

            view.TransactionId = view.CalculateHash();

            var chainState = blockchainRepository.GetChainState();

            if (height != chainState.Height + 1)
            {
                Logger.LogInformation("Discarding view #{height} (reason = invalid height)", view.Height);
                return false;
            }

            if (height > 0)
            {
                var lastView = blockchainRepository.GetLastView();
                var earliest = lastView.Timestamp + Constant.HEARTBEAT_INTERVAL;

                if (view.Timestamp < earliest)
                {
                    Logger.LogInformation("Discarding view #{height} (reason = timestamp too early)", view.Height);
                    return false;
                }
            }

            // Add any received votes for current view
            var votes = blockchainRepository.GetVotes(view.TransactionId);
            view.Votes.AddRange(votes);

            chainState.Height++;
            chainState.LastHash = view.TransactionId;
            chainState.Weight += chainState.CurrentDifficulty.ToWork() * votes.Count;

            blockchainRepository.Add(view);

            var sw = Stopwatch.StartNew();

            var toExecute = new List<Transaction>();

            blockchainRepository.Context.Entry(view)
                .Collection(x => x.Validates)
                .Load();

            foreach (var child in view.Validates)
            {
                if (child.Height is not null)
                {
                    continue;
                }

                TraverseTransaction(blockchainRepository.Context, child, height, toExecute);
            }

            var context = new ExecutorContext(blockchainRepository);
            var executor = ExecutorFactory.Create(context);

            if (height > 0)
            {
                var lastView = blockchainRepository.GetLastView();
                toExecute.Add(lastView);
            }

            executor.Execute(toExecute);

            Logger.LogInformation($"Finalized {toExecute.Count} transactions in {sw.ElapsedMilliseconds}ms");

            if (height > 0)
            {
                var blocks = blockchainRepository.GetBlocks(height);

                if (blocks.Count == 0)
                {
                    var work = chainState.CurrentDifficulty.ToWork();
                    var nextTarget = work / 4 * 3;
                    var minTarget = BigInteger.Pow(new BigInteger(2), Constant.STARTING_DIFFICULTY);

                    chainState.CurrentDifficulty = BigInteger.Max(minTarget, nextTarget).ToDifficulty();
                }
                else
                {
                    var totalWork = new BigInteger();

                    foreach (var block in blocks)
                    {
                        totalWork += block.Difficulty.ToWork();
                    }

                    chainState.Weight += totalWork;
                    chainState.CurrentDifficulty = totalWork.ToDifficulty();
                }
            }

            Logger.LogInformation($"Next difficulty {chainState.CurrentDifficulty}");

            blockchainRepository.SaveState(chainState);
            blockchainRepository.Context.SaveChanges();

            tx.Commit();

            ChainStateBroadcast.Post(chainState);

            var wallets = WalletManager.GetWallets();
            var addresses = new List<string>();

            foreach (var transaction in toExecute)
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
                if(wallets.TryGetValue(address, out var wallet))
                {
                    WalletBroadcast.Post(wallet);
                }
            }

            if (broadcast)
            {
                TransactionBroadcast.Post(new TransactionDto(view));
            }

            Logger.LogInformation($"Added view #{height}");

            return true;
        }
        catch (Exception ex)
        {
            tx.Rollback();
            Logger.LogError(ex, "AddView error");
        }

        return false;
    }

    public bool AddBlock(Blocktemplate blocktemplate, bool broadcast)
    {
        using var _ = rwlock.EnterWriteLockEx();
        using var blockchainRepository = new BlockchainRepository();
        using var tx = blockchainRepository.Context.Database.BeginTransaction();

        try
        {
            var block = Block.Create(blocktemplate.To, blocktemplate.Timestamp, blocktemplate.ParentHash, blocktemplate.Difficulty);

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

            block.Pow = blocktemplate.Solution;

            foreach (var txhash in blocktemplate.Validates)
            {
                var transaction = blockchainRepository.Get<Transaction>(txhash);

                if (transaction is null)
                {
                    Logger.LogInformation("AddBlock rejected (reason = unknown transaction reference)");
                    return false;
                }

                block.Validates.Add(transaction);
            }

            if (block.Validates.Count < 2)
            {
                Logger.LogInformation("AddBlock rejected (reason = not enought transactions referenced)");
                return false;
            }

            block.TransactionId = block.CalculateHash();

            var chainState = blockchainRepository.GetChainState();

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

            var lastView = blockchainRepository.GetLastView();

            if (block.Timestamp < lastView.Timestamp)
            {
                Logger.LogInformation("AddBlock rejected (reason = invalid timestamp)");
                return false;
            }

            var exists = blockchainRepository.Get<Block>(block.TransactionId);

            if (exists is not null)
            {
                Logger.LogInformation("AddBlock rejected (reason = already exists)");
                return false;
            }

            if (!block.VerifyNonce())
            {
                Logger.LogInformation("AddBlock rejected (reason = invalid nonce)");
                return false;
            }

            var to = blockchainRepository.GetWallet(block.To) ?? new LedgerWallet(block.To);

            checked
            {
                to.Pending += block.Value;
            }

            var wallet = WalletManager.GetWallet(to.Address.ToString());

            if (wallet is not null)
            {
                WalletBroadcast.Post(wallet);
            }

            chainState.Blocks++;

            blockchainRepository.Add(block);
            blockchainRepository.SaveState(chainState);
            blockchainRepository.Context.SaveChanges();

            tx.Commit();

            if (broadcast)
            {
                TransactionBroadcast.Post(new TransactionDto(block));
            }

            Logger.LogInformation($"Added block #{chainState.Blocks} [diff = {block.Difficulty}]");

            return true;
        }
        catch (Exception ex)
        {
            tx.Rollback();
            Logger.LogError(ex, "AddView error");
        }

        return false;
    }

    public bool AddTransaction(TransactionDto tx, bool broadcast)
    {
        using var _ = rwlock.EnterWriteLockEx();
        using var blockchainRepository = new BlockchainRepository();
        using var dbTx = blockchainRepository.Context.Database.BeginTransaction();

        try
        {
            if (tx.TransactionType == TransactionType.PAYMENT && tx.Value == 0)
            {
                Logger.LogInformation("AddTransaction rejected (reason = zero payment)");
                return false;
            }

            if (tx.Validates.Count < 2)
            {
                Logger.LogInformation("AddTransaction rejected (reason = not enought transactions referenced)");
                return false;
            }

            var lastView = blockchainRepository.GetLastView();

            if (tx.Timestamp < lastView.Timestamp)
            {
                Logger.LogInformation("AddTransaction rejected (reason = invalid timestamp)");
                return false;
            }

            var validates = new List<Transaction>();

            foreach (var txId in tx.Validates)
            {
                var parentTx = blockchainRepository.Get<Transaction>(txId);

                if (parentTx is null)
                {
                    Logger.LogInformation("AddTransaction rejected (reason = unknown transaction reference)");
                    return false;
                }

                validates.Add(parentTx);
            }

            var transaction = new Transaction(tx, validates);

            if (transaction.To is null)
            {
                Logger.LogInformation("AddTransaction rejected (reason = null 'to' address)");
                return false;
            }

            if (!transaction.Verify())
            {
                Logger.LogInformation("AddTransaction rejected (reason = signature verification failed)");
                return false;
            }

            var exists = blockchainRepository.Get<Transaction>(transaction.TransactionId);

            if (exists is not null)
            {
                Logger.LogInformation("AddTransaction rejected (reason = already exists)");
                return false;
            }

            var from = blockchainRepository.GetWallet(transaction.From);

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

            var to = blockchainRepository.GetWallet(transaction.To) ?? new LedgerWallet(transaction.To);

            checked
            {
                from.Balance -= transaction.Value;
                to.Pending += transaction.Value;
            }

            var fromWallet = WalletManager.GetWallet(from.Address.ToString());

            if (fromWallet is not null)
            {
                WalletBroadcast.Post(fromWallet);
            }

            var toWallet = WalletManager.GetWallet(to.Address.ToString());

            if (toWallet is not null)
            {
                WalletBroadcast.Post(toWallet);
            }

            blockchainRepository.Add(transaction);
            blockchainRepository.UpdateWallets(from, to);
            blockchainRepository.Context.SaveChanges();

            dbTx.Commit();

            var wallets = WalletManager.GetWallets();

            if (wallets.TryGetValue(transaction.From.ToString(), out var wallet))
            {
                wallet.Balance = from.Balance;

                WalletBroadcast.Post(wallet);
            }

            WalletManager.UpdateWallets(wallets.Values);

            Logger.LogInformation($"Added transaction [type = {tx.TransactionType}, value = {tx.Value}]");

            if (broadcast)
            {
                TransactionBroadcast.Post(tx);
            }

            return true;
        }
        catch (Exception ex) 
        {
            dbTx.Rollback();
            Logger.LogError(ex, "AddTransaction error");
        }

        return false;
    }

    public bool AddVote(Vote vote, bool broadcast)
    {
        if (!vote.Verify())
        {
            Logger.LogInformation("AddVote rejected (reason = invalid signature)");
            return false;
        }

        using var _ = rwlock.EnterWriteLockEx();
        using var blockchainRepository = new BlockchainRepository();

        var chainState = blockchainRepository.GetChainState();
        var view = blockchainRepository.Get<View>(vote.TransactionId);

        if (view is not null && view.Height < chainState.Height)
        {
            Logger.LogInformation("AddVote rejected (reason = references finalized view)");
            return false;
        }

        blockchainRepository.AddVote(vote);

        if (broadcast)
        {
            VoteBroadcast.Post(vote);
        }

        return true;
    }

    private void TraverseTransaction(BlockchainContext context, Transaction transaction, long height, List<Transaction> toExecute)
    {
        transaction.Height = height;

        if (context.Entry(transaction).State == EntityState.Detached)
        {
            context.Update(transaction);
        }

        context.Entry(transaction)
            .Collection(x => x.Validates)
            .Load();

        foreach (var tx in transaction.Validates)
        {
            if (tx.Height is not null)
            {
                continue;
            }

            TraverseTransaction(context, tx, height, toExecute);
        }

        toExecute.Add(transaction);
    }

    public View GetLastView()
    {
        using var _ = rwlock.EnterReadLockEx();
        using var blockchainRepository = new BlockchainRepository();

        return blockchainRepository.GetLastView();
    }

    public List<Transaction> GetTransactionToValidate()
    {
        using var _ = rwlock.EnterReadLockEx();
        using var blockchainRepository = new BlockchainRepository();

        return blockchainRepository.GetTransactionsToValidate();
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
        using var blockchainRepository = new BlockchainRepository();

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var chainState = blockchainRepository.GetChainState();
        var block = Block.Create(wallet, timestamp, chainState.LastHash, chainState.CurrentDifficulty);

        block.Validates = blockchainRepository.GetTransactionsToValidate();

        return new Blocktemplate
        {
            Height = chainState.Height,
            To = wallet,
            Difficulty = chainState.CurrentDifficulty,
            ParentHash = block.ParentHash,
            Nonce = block.GetHash(),
            Timestamp = block.Timestamp,
            Validates = block.Validates.Select(x => x.TransactionId).ToList(),
            Data = block.Data ?? Array.Empty<byte>()
        };
    }

    public ChainState GetChainState()
    {
        using var _ = rwlock.EnterReadLockEx();
        using var blockchainRepository = new BlockchainRepository();

        return blockchainRepository.GetChainState();
    }

    public Difficulty GetCurrentDifficulty()
    {
        using var _ = rwlock.EnterReadLockEx();
        using var blockchainRepository = new BlockchainRepository();

        var chainState = blockchainRepository.GetChainState();
        return chainState.CurrentDifficulty;
    }

    public long GetCurrentHeight()
    {
        using var _ = rwlock.EnterReadLockEx();
        using var blockchainRepository = new BlockchainRepository();

        var chainState = blockchainRepository.GetChainState();
        return chainState.Height;
    }

    public ulong GetBalance(Address address)
    {
        using var _ = rwlock.EnterReadLockEx();
        using var blockchainRepository = new BlockchainRepository();

        if (address.IsContract())
        {
            return blockchainRepository.GetContract(address)?.Balance ?? 0;
        }

        return blockchainRepository.GetWallet(address)?.Balance ?? 0;
    }

    /*public BigInteger GetTotalWork()
    {
        using var _ = rwlock.EnterReadLockEx();
        using var blockchainRepository = new BlockchainRepository();

        var chainState = blockchainRepository.GetChainState();
        return chainState.TotalWork;
    }

    public List<PowBlock> GetLastBlocks(int count)
    {
        using var _ = rwlock.EnterReadLockEx();
        using var blockchainRepository = new BlockchainRepository();

        return blockchainRepository.Tail(count);
    }

    public List<PowBlock> GetLastBlocks(long start, int count)
    {
        using var _ = rwlock.EnterReadLockEx();
        using var blockchainRepository = new BlockchainRepository();

        return blockchainRepository.Tail(start, count);
    }

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

    public List<PosBlock> GetPosFrom(long id)
    {
        using var _ = rwlock.EnterReadLockEx();
        using var blockchainRepository = new BlockchainRepository();

        return blockchainRepository.GetPosFrom(id);
    }

    public List<PowBlock> GetPowFrom(long id)
    {
        using var _ = rwlock.EnterReadLockEx();
        using var blockchainRepository = new BlockchainRepository();

        return blockchainRepository.GetPowFrom(id);
    }

    public void ResetChain()
    {
        using var _ = rwlock.EnterWriteLockEx();
        using var blockchainRepository = new BlockchainRepository();

        blockchainRepository.Context.Database.EnsureDeleted();
        blockchainRepository.Context.Database.Migrate();

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
    }*/

    public Contract? GetContract(Address address)
    {
        using var _ = rwlock.EnterReadLockEx();
        using var blockchainRepository = new BlockchainRepository();

        return blockchainRepository.GetContract(address, true);
    }

    public List<LedgerWallet> GetRichList(int count)
    {
        using var _ = rwlock.EnterReadLockEx();
        using var blockchainRepository = new BlockchainRepository();

        return blockchainRepository.GetRichList(count);
    }
    public List<Transaction> GetTransactionsForAddress(Address address)
    {
        using var _ = rwlock.EnterReadLockEx();
        using var blockchainRepository = new BlockchainRepository();

        return blockchainRepository.GetTransactions(address);
    }

    public Transaction? GetTransactionForHash(SHA256Hash hash)
    {
        using var _ = rwlock.EnterReadLockEx();
        using var blockchainRepository = new BlockchainRepository();

        return blockchainRepository.Get<Transaction>(hash);
    }

    public LedgerWallet? GetLedgerWallet(Address address)
    {
        using var _ = rwlock.EnterReadLockEx();
        using var blockchainRepository = new BlockchainRepository();

        return blockchainRepository.GetWallet(address);
    }
    
    public string? CallContractMethod(Address address, CallMethod call)
    {
        using var _ = rwlock.EnterReadLockEx();
        using var blockchainRepository = new BlockchainRepository();

        var contract = blockchainRepository.GetContract(address) ?? throw new Exception(ExecutionResult.INVALID_CONTRACT.ToString());

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
        using var blockchainRepository = new BlockchainRepository();

        return blockchainRepository.GetToken(tokenId);
    }

    public List<Token> GetTokens(Address address)
    {
        using var _ = rwlock.EnterReadLockEx();
        using var blockchainRepository = new BlockchainRepository();

        return blockchainRepository.GetTokens(address);
    }

    public List<Token> GetContractTokens(Address contractAddress)
    {
        using var _ = rwlock.EnterReadLockEx();
        using var blockchainRepository = new BlockchainRepository();

        return blockchainRepository.GetContractTokens(contractAddress);
    }

    private void RollbackEffectBalance(BlockchainRepository repository, Dictionary<string, LedgerWallet> walletCache, Contract contract, Effect effect)
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

    private void RollbackTokenEffect(BlockchainRepository repository, Dictionary<string, LedgerWallet> walletCache, Contract contract, Effect effect)
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
            repository.Context.Remove<Token>(token);
        }
        else
        {
            token.Wallet = fromWallet;
        }
    }

    public IDisposable OnChainUpdated(ITargetBlock<ChainState> action)
    {
        return ChainStateBroadcast.LinkTo(action);
    }

    public IDisposable OnWalletUpdated(ITargetBlock<Wallet> action)
    {
        return WalletBroadcast.LinkTo(action);
    }

    public IDisposable OnVoteAdded(ITargetBlock<Vote> action)
    {
        return VoteBroadcast.LinkTo(action);
    }

    public IDisposable OnTokenTransferred(ITargetBlock<TransferTokenEventArgs> action)
    {
        return TokenTransferredBroadcast.LinkTo(action);
    }

    public IDisposable OnTokenConsumed(ITargetBlock<ConsumeTokenEventArgs> action)
    {
        return TokenConsumedBroadcast.LinkTo(action);
    }
}
