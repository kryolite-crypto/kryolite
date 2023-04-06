using System.Numerics;
using System.Threading.Tasks.Dataflow;
using Kryolite.Shared;
using Microsoft.Extensions.Logging;

namespace Kryolite.Node;

public class BlockchainManager : IBlockchainManager
{
    private readonly INetworkManager discoveryManager;
    private readonly IMempoolManager mempoolManager;
    private readonly IWalletManager walletManager;
    private readonly ILogger<BlockchainManager> logger;
    private readonly ReaderWriterLockSlim rwlock = new(LockRecursionPolicy.SupportsRecursion);

    private BroadcastBlock<ChainState> ChainStateBroadcast = new(i => i);
    private BroadcastBlock<PosBlock> BlockBroadcast = new(i => i);
    private BroadcastBlock<Wallet> WalletBroadcast = new(i => i);
    private BroadcastBlock<Vote> VoteBroadcast = new(i => i);
    private BroadcastBlock<TransferTokenEventArgs> TokenTransferredBroadcast = new(i => i);
    private BroadcastBlock<ConsumeTokenEventArgs> TokenConsumedBroadcast = new(i => i);

    public BlockchainManager(INetworkManager discoveryManager, IMempoolManager mempoolManager, IWalletManager walletManager, ILogger<BlockchainManager> logger)
    {
        this.discoveryManager = discoveryManager ?? throw new ArgumentNullException(nameof(discoveryManager));
        this.mempoolManager = mempoolManager ?? throw new ArgumentNullException(nameof(mempoolManager));
        this.walletManager = walletManager ?? throw new ArgumentNullException(nameof(walletManager));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public PowBlock? GetPowBlock(long id)
    {
        using var _ = rwlock.EnterReadLockEx();
        using var blockchainRepository = new BlockchainRepository();

        return blockchainRepository.GetPowBlock(id);
    }

    public PosBlock? GetPosBlock(long id)
    {
        using var _ = rwlock.EnterReadLockEx();
        using var blockchainRepository = new BlockchainRepository();

        return blockchainRepository.GetPosBlock(id);
    }

    public bool AddBlock(PosBlock block, bool broadcastBlock = true, bool broadcastVote = true)
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

            var powExcecutor = Executor.Create<PowBlock, BlockchainExContext>(blockchainContext)
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

            var txContext = new TransactionContext(blockchainRepository, wallets, logger)
            {
                Height = block.Pow?.Height ?? 0,
                Fee = block.Pow?.Transactions.Where(x => x.TransactionType == TransactionType.PAYMENT).Select(x => x.MaxFee).DefaultIfEmpty().Min() ?? 0,
                FeeTotal = (ulong)(block.Pow?.Transactions.Where(x => x.TransactionType == TransactionType.PAYMENT).Select(x => (long)x.MaxFee).DefaultIfEmpty().Sum() ?? 0L),
                Timestamp = block.Timestamp,
                Seed = (int)seed
            };

            if (block.Pow is not null)
            {
                var txExecutor = Executor.Create<Transaction, TransactionContext>(txContext)
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

                walletManager.UpdateWallets(txContext.Wallets.Select(x => x.Value).Where(x => x.Updated));
                blockchainRepository.UpdateWallets(txContext.LedgerWalletCache.Select(x => x.Value));
                blockchainRepository.UpdateContracts(txContext.ContractCache.Select(x => x.Value));

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

            if (broadcastVote && block.Pow is not null /*&& pos is active*/)
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

    private bool AddBlocks(List<PosBlock> blocks, BlockchainRepository blockchainRepository)
    {
        var chainState = blockchainRepository.GetChainState();
        var wallets = walletManager.GetWallets();

        var blockchainContext = new BlockchainExContext()
        {
            LastBlocks = blockchainRepository.Tail(blocks.Select(x => x.Height).Min(), 11),
            CurrentDifficulty = chainState.POW.CurrentDifficulty,
            NetworkTime = discoveryManager.GetNetworkTime()
        };

        var powExcecutor = Executor.Create<PowBlock, BlockchainExContext>(blockchainContext)
            .Link<VerifyDifficulty>()
            .Link<VerifyNonce>()
            .Link<VerifyId>(x => x.Height > 0)
            .Link<VerifyParentHash>(x => x.Height > 0)
            .Link<VerifyTimestampPast>(x => x.Height > 0)
            .Link<VerifyTimestampFuture>(x => x.Height > 0);
        
        var txContext = new TransactionContext(blockchainRepository, wallets, logger);

        // TODO: This needs refactoring
        var txExecutor = Executor.Create<Transaction, TransactionContext>(txContext)
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

        var epochStart = blockchainRepository.GetPowBlock(chainState.POW.Height - (chainState.POW.Height % Constant.EPOCH_LENGTH_BLOCKS) + 1);

        int progress = 0;
        foreach (var block in blocks)
        {
            var seed = blockchainContext.LastBlocks.TakeLast(11).Average(x => x.Timestamp);

            if (block.Pow != null)
            {
                if (!powExcecutor.Execute(block.Pow, out var result)) {
                    logger.LogWarning($"AddBlock ({block.Pow.Height}) failed with: {result}");
                    return false;
                }

                txContext.Height = block.Pow.Height;
                txContext.Fee = block.Pow.Transactions.Where(x => x.TransactionType == TransactionType.PAYMENT).Select(x => x.MaxFee).DefaultIfEmpty().Min();
                txContext.FeeTotal = (ulong)block.Pow.Transactions.Where(x => x.TransactionType == TransactionType.PAYMENT).Select(x => (long)x.MaxFee).DefaultIfEmpty().Sum();
                txContext.Timestamp = block.Pow.Timestamp;
                txContext.Seed = (int)seed;

                if (!txExecutor.ExecuteBatch(block.Pow.Transactions, out var res)) {
                    logger.LogWarning($"AddBlock failed with: {res}");
                    return false;
                }

                if (block.Height % Constant.EPOCH_LENGTH_BLOCKS == 0) {
                    NextEpoch(epochStart, block.Pow, chainState);
                    blockchainContext.CurrentDifficulty = chainState.POW.CurrentDifficulty;
                }

                if (block.Height % Constant.EPOCH_LENGTH_BLOCKS == 1) {
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

            ChainObserver.ReportProgress(++progress, blocks.Count);
        }

        walletManager.UpdateWallets(txContext.Wallets.Select(x => x.Value).Where(x => x.Updated));

        blockchainRepository.UpdateWallets(txContext.LedgerWalletCache.Select(x => x.Value));
        blockchainRepository.UpdateContracts(txContext.ContractCache.Select(x => x.Value));
        blockchainRepository.Add(blocks, chainState);

        mempoolManager.RemoveTransactions(blocks.SelectMany(x => x.Transactions).Where(x => x.TransactionType == TransactionType.PAYMENT));

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

        return true;
    }

    public Blocktemplate GetBlocktemplate(Address wallet)
    {
        using var _ = rwlock.EnterReadLockEx();
        using var blockchainRepository = new BlockchainRepository();

        var chainState = blockchainRepository.GetChainState();

        var transactions = mempoolManager.GetTransactions();

        var rand = new Random();

        var timestamp = DateTimeOffset.Now.ToUnixTimeSeconds();

        var block = new PowBlock {
            Height = chainState.POW.Height + 1,
            ParentHash = chainState.POW.LastHash,
            Timestamp = new DateTimeOffset(DateTime.Now).ToUnixTimeSeconds(),
            Difficulty = chainState.POW.CurrentDifficulty,
            Transactions = transactions
        };

        transactions.Add(new Transaction {
            TransactionType = TransactionType.MINER_FEE,
            To = wallet,
            Value = (ulong)(1000000000 * Constant.MINER_FEE),
            Nonce = rand.Next(int.MinValue, int.MaxValue)
        });

        return new Blocktemplate
        {
            Height = block.Height,
            Difficulty = chainState.POW.CurrentDifficulty,
            ParentHash = block.ParentHash,
            Nonce = block.GetHash(),
            Timestamp = timestamp,
            Transactions = transactions
        };
    }

    public Difficulty GetCurrentDifficulty()
    {
        using var _ = rwlock.EnterReadLockEx();
        using var blockchainRepository = new BlockchainRepository();

        var chainState = blockchainRepository.GetChainState();
        return chainState.POW.CurrentDifficulty;
    }

    public long GetCurrentHeight()
    {
        using var _ = rwlock.EnterReadLockEx();
        using var blockchainRepository = new BlockchainRepository();

        // TODO: return actual height
        return blockchainRepository.Count();
    }

    public SHA256Hash? GetLastBlockhash()
    {
        using var _ = rwlock.EnterReadLockEx();
        using var blockchainRepository = new BlockchainRepository();

        return blockchainRepository.Last()?.GetHash();
    }

    public ulong GetBalance(Address address)
    {
        using var _ = rwlock.EnterReadLockEx();
        using var blockchainRepository = new BlockchainRepository();

        if (address.IsContract()) {
            return blockchainRepository.GetContract(address)?.Balance ?? 0;
        }

        return blockchainRepository.GetWallet(address)?.Balance ?? 0;
    }

    public List<Transaction> AddTransactionsToQueue(IList<Transaction> transactions, bool broadcast = true)
    {
        using var _ = rwlock.EnterReadLockEx();
        using var blockchainRepository = new BlockchainRepository();

        var context = new TransactionContext(blockchainRepository, mempoolManager, logger);

        var executor = Executor.Create<Transaction, TransactionContext>(context)
            .Link<NotReward>()
            .Link<CheckMinFee>()
            .Link<VerifySignature>()
            // TODO: Check for duplicate tx
            .Link<FetchSenderWallet>(x => x.TransactionType == TransactionType.PAYMENT)
            .Link<HasFunds>(x => x.TransactionType == TransactionType.PAYMENT);
            // TODO: HasAsset

        var newTransactions = transactions.Where(tx => !mempoolManager.HasTransaction(tx));
        var valid = executor.Execute(newTransactions);

        mempoolManager.AddTransactions(valid, broadcast);

        logger.LogInformation($"Added {valid.Count} transactions to queue");

        return valid;
    }

    public void AddTransactionsToQueue(Transaction transaction) {
        AddTransactionsToQueue(new List<Transaction>() { transaction });
    }

    private void NextEpoch(PowBlock? epochStart, PowBlock epochEnd, ChainState chainState)
    {
        if (chainState.POW.Height == 0) {
            // Starting difficulty
            chainState.POW.CurrentDifficulty = new Difficulty { b0 = Constant.STARTING_DIFFICULTY };
            return;
        }

        if (epochStart == null) {
            throw new Exception("Epoch Start block not found");
        }

        var elapsed = epochEnd.Timestamp - epochStart.Timestamp;
        var expected = Constant.TARGET_BLOCK_TIME_S * Constant.EPOCH_LENGTH_BLOCKS;

        var newDiff = chainState.POW.CurrentDifficulty.ToWork() * new BigInteger(expected / (double)elapsed);
        chainState.POW.CurrentDifficulty = newDiff.ToDifficulty();

        logger.LogInformation($"Epoch {epochEnd.Height / 100 + 1}: difficulty {BigInteger.Log(newDiff, 2)}, target = {newDiff}");
    }

    public BigInteger GetTotalWork()
    {
        using var _ = rwlock.EnterReadLockEx();
        using var blockchainRepository = new BlockchainRepository();

        var chainState = blockchainRepository.GetChainState();
        return chainState.POW.TotalWork;
    }

    public ChainState GetChainState()
    {
        using var _ = rwlock.EnterReadLockEx();
        using var blockchainRepository = new BlockchainRepository();

        return blockchainRepository.GetChainState();
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
        using var blockchainRepository = new BlockchainRepository();
        using var txContext = blockchainRepository.Context.Database.BeginTransaction();

        try
        {
            var sortedBlocks = blocks.Where(x => x.Height > 0)
                .OrderBy(x => x.Height)
                .ToList();

            var chainState = blockchainRepository.GetChainState();

            var min = sortedBlocks.Min(x => x.Height);
            var max = chainState.POS.Height;

            var ledgerWallets = new Dictionary<string, LedgerWallet>();
            var wallets = walletManager.GetWallets();

            long progress = 0;
            ChainObserver.ReportProgress(progress, sortedBlocks.Count);

            for (long i = max; i >= min; i--)
            {
                var cBlock = blockchainRepository.GetPosBlock(i);

                if (cBlock == null)
                {
                    continue;
                }

                if (cBlock.Pow is not null)
                {
                    var powFee = cBlock.Pow.Transactions.DefaultIfEmpty().Select(x => x?.MaxFee ?? 0UL).Min();

                    foreach (var tx in cBlock.Pow.Transactions) 
                    {
                        if(tx.PublicKey != null) 
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

                        var recipientAddr = tx.To.ToString();
                        if (!ledgerWallets.ContainsKey(recipientAddr)) 
                        {
                            ledgerWallets.Add(recipientAddr, blockchainRepository.GetWallet(tx.To));
                        }

                        if (tx.To.IsContract()) 
                        {
                            var contract = blockchainRepository.GetContract(tx.To);

                            if (contract is not null)
                            {
                                foreach (var effect in tx.Effects)
                                {
                                    if (!ledgerWallets.ContainsKey(effect.To.ToString())) 
                                    {
                                        ledgerWallets.Add(effect.To.ToString(), blockchainRepository.GetWallet(effect.To));
                                    }

                                    var eWallet = ledgerWallets[effect.To.ToString()];

                                    checked
                                    {
                                        eWallet.Balance -= effect.Value;
                                        contract.Balance += effect.Value;
                                    }
                                }
                            }
                        }
                        else
                        {
                            var recipient = ledgerWallets[recipientAddr];

                            recipient.Balance = checked(recipient.Balance - tx.Value);

                            if (tx.TransactionType == TransactionType.MINER_FEE)
                            {
                                recipient.Balance = checked(recipient.Balance - (powFee * (ulong)cBlock.Transactions.LongCount()));
                            }

                            if(wallets.TryGetValue(tx.To.ToString(), out var rWallet))
                            {
                                rWallet.Balance = recipient.Balance;
                                rWallet.WalletTransactions.RemoveAll(x => x.Height == cBlock.Height);
                                rWallet.Updated = true;
                            }
                        }
                    }

                    chainState.POW.Height = cBlock.Pow.Height - 1;
                    chainState.POW.TotalWork -= cBlock.Pow.Difficulty.ToWork();
                    chainState.POW.CurrentDifficulty = cBlock.Pow.Difficulty;
                    chainState.POW.LastHash = blockchainRepository.GetPowBlock(chainState.POW.Height)?.GetHash() ?? new SHA256Hash();
                }

                var fee = cBlock.Transactions.DefaultIfEmpty().Select(x => x?.MaxFee ?? 0UL).Min();

                foreach (var tx in cBlock.Transactions) 
                {
                    if(tx.PublicKey != null) 
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
                            sender.Balance += fee;
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
                        var contract = blockchainRepository.GetContract(tx.To);

                        if (contract is not null)
                        {
                            foreach (var effect in tx.Effects)
                            {
                                if (effect.Value > 0)
                                {
                                    RollbackEffectBalance(blockchainRepository, ledgerWallets, contract, effect);
                                }

                                if (effect.TokenId is not null)
                                {
                                    RollbackEffectToken(blockchainRepository, ledgerWallets, contract, effect);
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
                            recipient.Balance = checked(recipient.Balance - (fee * (ulong)cBlock.Transactions.LongCount()));
                        }

                        if(wallets.TryGetValue(tx.To.ToString(), out var rWallet))
                        {
                            rWallet.Balance = recipient.Balance;
                            rWallet.WalletTransactions.RemoveAll(x => x.Height == cBlock.Height);
                            rWallet.Updated = true;
                        }
                    }
                }

                chainState.POS.Height = cBlock.Height - 1;
                chainState.POS.LastHash = blockchainRepository.GetPosBlock(chainState.POS.Height)?.GetHash() ?? new SHA256Hash();

                blockchainRepository.UpdateWallets(ledgerWallets.Values);
                blockchainRepository.Delete(cBlock);

                ChainObserver.ReportProgress(++progress, sortedBlocks.Count);
            }

            if (wallets.Values.Count > 0) {
                walletManager.RollbackWallets(wallets.Values.ToList(), min);
            }

            blockchainRepository.DeleteContractSnapshot(chainState.POS.Height);
            blockchainRepository.SaveState(chainState);

            progress = 0;
            ChainObserver.ReportProgress(progress, sortedBlocks.Count);

            if(!AddBlocks(sortedBlocks, blockchainRepository))
            {
                logger.LogError($"Set chain failed");
                txContext.Rollback();
                return false;
            }

            txContext.Commit();

            foreach (var wallet in wallets.Select(x => x.Value).Where(x => x.Updated))
            {
                WalletBroadcast.Post(wallet);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Chain reorg failure");
            txContext.Rollback();
        }

        logger.LogInformation("Chain synchronization completed");
        return true;
    }

    public bool AddVote(Vote vote)
    {
        if (!vote.Verify())
        {
            logger.LogWarning("Vote rejected (invalid signature)");
            // TODO: file complaint
            return false;
        }

        using var _ = rwlock.EnterWriteLockEx();
        using var blockchainRepository = new BlockchainRepository();

        blockchainRepository.AddVote(vote);

        return true;
    }

    public List<Vote> AddVotes(IList<Vote> votes)
    {
        var valid = new List<Vote>();

        using var _ = rwlock.EnterWriteLockEx();
        using var blockchainRepository = new BlockchainRepository();

        foreach (var vote in votes)
        {
            if (!vote.Verify())
            {
                logger.LogWarning("Vote rejected (invalid signature)");
                // file complaint
                continue;
            }

            if (!blockchainRepository.VoteExists(vote.Signature))
            {
                valid.Add(vote);
            }
        }

        // TODO check for duplicates
        blockchainRepository.AddVotes(valid);

        return valid;
    }

    public List<PosBlock> GetPosFrom(long id)
    {
        using var _ = rwlock.EnterReadLockEx();
        using var blockchainRepository = new BlockchainRepository();

        return blockchainRepository.GetPosFrom(id);
    }

    public void ResetChain()
    {
        using var _ = rwlock.EnterWriteLockEx();
        using var blockchainRepository = new BlockchainRepository();

        blockchainRepository.Context.Database.EnsureDeleted();
        blockchainRepository.Context.Database.EnsureCreated();
    }

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

        return blockchainRepository.GetTransaction(hash);
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

        var contract = blockchainRepository.GetContract(address) ?? throw new ExecutionException(ExecutionResult.INVALID_CONTRACT);

        var snapshot = contract.Snapshots
            .OrderByDescending(x => x.Height)
            .FirstOrDefault();

        if (snapshot == null)
        {
            throw new ExecutionException(ExecutionResult.CONTRACT_SNAPSHOT_MISSING);
        }

        var methodName = $"{call.Method}";
        var method = contract.Manifest.Methods
            .Where(x => x.Name == methodName)
            .FirstOrDefault();

        if (method == null)
        {
            throw new ExecutionException(ExecutionResult.INVALID_METHOD);
        }

        if (!method.IsReadonly)
        {
            throw new Exception("only readonly methods can be called without transaction");
        }

        var methodParams = new List<object> { contract.EntryPoint ?? throw new ExecutionException(ExecutionResult.CONTRACT_ENTRYPOINT_MISSING) };

        if (call.Params is not null)
        {
            methodParams.AddRange(call.Params);
        }

        var vmContext = new VMContext(contract, new Transaction { To = address }, 0, logger);

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

    private void RollbackEffectToken(BlockchainRepository repository, Dictionary<string, LedgerWallet> walletCache, Contract contract, Effect effect)
    {
        if (!walletCache.ContainsKey(effect.From.ToString()))
        {
            walletCache.Add(effect.From.ToString(), repository.GetWallet(effect.From));
        }

        var fromWallet = walletCache[effect.From.ToString()];

        var token = repository.GetToken(effect.TokenId);

        if (effect.From == contract.Address)
        {
            if (token is not null)
            {
                repository.Context.Remove<Token>(token);
            }
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

    public IDisposable OnBlockAdded(ITargetBlock<PosBlock> action)
    {
        return BlockBroadcast.LinkTo(action);
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
