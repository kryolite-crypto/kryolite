using System.Numerics;
using System.Threading.Tasks.Dataflow;
using Marccacoin.Shared;
using Microsoft.Extensions.Logging;

namespace Marccacoin;

public class BlockchainManager : IBlockchainManager
{
    private readonly INetworkManager discoveryManager;
    private readonly IMempoolManager mempoolManager;
    private readonly IWalletManager walletManager;
    private readonly ILogger<BlockchainManager> logger;
    private readonly ReaderWriterLockSlim rwlock = new(LockRecursionPolicy.SupportsRecursion);

    private BroadcastBlock<PosBlock> BlockBroadcast = new(i => i);
    private BroadcastBlock<Wallet> WalletBroadcast = new(i => i);
    private BroadcastBlock<Vote> VoteBroadcast = new(i => i);

    public BlockchainManager(INetworkManager discoveryManager, IMempoolManager mempoolManager, IWalletManager walletManager, ILogger<BlockchainManager> logger)
    {
        this.discoveryManager = discoveryManager ?? throw new ArgumentNullException(nameof(discoveryManager));
        this.mempoolManager = mempoolManager ?? throw new ArgumentNullException(nameof(mempoolManager));
        this.walletManager = walletManager ?? throw new ArgumentNullException(nameof(walletManager));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public PowBlock? GetBlock(long id)
    {
        using var _ = rwlock.EnterReadLockEx();
        using var blockchainRepository = new BlockRepository();

        return blockchainRepository.GetBlock(id);
    }

    public bool AddBlock(PosBlock block, bool broadcastBlock = true)
    {
        using var _ = rwlock.EnterWriteLockEx();
        using var blockchainRepository = new BlockRepository(true);

        var chainState = blockchainRepository.GetChainState();

        var blockchainContext = new BlockchainContext()
        {
            LastBlocks = blockchainRepository.Tail(11),
            CurrentDifficulty = chainState.POW.CurrentDifficulty,
            NetworkTime = discoveryManager.GetNetworkTime()
        };

        var powExcecutor = Executor.Create<PowBlock, BlockchainContext>(blockchainContext)
            .Link<VerifyDifficulty>()
            .Link<VerifyNonce>()
            .Link<VerifyId>(x => x.Id > 0)
            .Link<VerifyParentHash>(x => x.Id > 0)
            .Link<VerifyTimestampPast>(x => x.Id > 0)
            .Link<VerifyTimestampFuture>(x => x.Id > 0);

        if (block.Pow != null && !powExcecutor.Execute(block.Pow, out var result)) {
            logger.LogWarning($"AddBlock failed with: {result}");
            return false;
        }

        // POS
        // Verify Id
        // Verify ParentHash
        // Verify Timestamp (must be more then median of 11 last pos blocks?)
        
        // If has POW
        // Verify Transactions as votes
        // Credit Block Reward to Miner (create transaction)
        // Credit Verifier reward to signers (create transaction)
        // Credit Dev Fee (create transaction)

        // If not has POW
        // Verify Transactions
        // Execute transactions
        // Credit tx fees to pos node
        // Sign block and collect transaction fees

        var wallets = walletManager.GetWallets();

        var txContext = new TransactionContext(blockchainRepository, wallets)
        {
            Fee = block.Transactions.Where(x => x.TransactionType == TransactionType.PAYMENT).Select(x => x.MaxFee).DefaultIfEmpty().Min(),
            FeeTotal = (ulong)block.Transactions.Where(x => x.TransactionType == TransactionType.PAYMENT).Select(x => (long)x.MaxFee).DefaultIfEmpty().Sum(),
            Timestamp = block.Timestamp
        };

        var txExecutor = Executor.Create<Transaction, TransactionContext>(txContext)
            .Link<VerifyBlockReward>(x => x.TransactionType == TransactionType.MINER_FEE)
            .Link<VerifyValidatorReward>(x => x.TransactionType == TransactionType.VALIDATOR_FEE)
            .Link<VerifyDevFee>(x => x.TransactionType == TransactionType.DEV_FEE)
            .Link<VerifySignature>(x => x.TransactionType == TransactionType.PAYMENT)
            // TODO: Check for duplicate tx
            .Link<FetchSenderWallet>(x => x.TransactionType == TransactionType.PAYMENT)
            .Link<TakeBalanceFromSender>(x => x.TransactionType == TransactionType.PAYMENT)
            .Link<UpdateSenderWallet>(x => x.TransactionType == TransactionType.PAYMENT)
            .Link<FetchRecipientWallet>()
            .Link<AddBlockRewardToRecipient>(x => x.TransactionType == TransactionType.MINER_FEE)
            .Link<AddBalanceToRecipient>()
            .Link<UpdateRecipientWallet>();

        if (!txExecutor.ExecuteBatch(block.Transactions, out var txresult)) {
            logger.LogWarning($"AddBlock failed with: {txresult}");
            return false;
        }

        walletManager.UpdateWallets(txContext.Wallets.Select(x => x.Value).Where(x => x.Updated));
        blockchainRepository.UpdateWallets(txContext.LedgerWalletCache.Select(x => x.Value));

        logger.LogInformation($"Added block {block.Height}");

        if (block.Pow is not null) 
        {
            txContext = new TransactionContext(blockchainRepository, wallets)
            {
                Fee = block.Pow.Transactions.Where(x => x.TransactionType == TransactionType.PAYMENT).Select(x => x.MaxFee).DefaultIfEmpty().Min(),
                FeeTotal = (ulong)block.Pow.Transactions.Where(x => x.TransactionType == TransactionType.PAYMENT).Select(x => (long)x.MaxFee).DefaultIfEmpty().Sum(),
                Timestamp = block.Timestamp
            };

            txExecutor = Executor.Create<Transaction, TransactionContext>(txContext)
                .Link<VerifyBlockReward>(x => x.TransactionType == TransactionType.MINER_FEE)
                .Link<VerifyValidatorReward>(x => x.TransactionType == TransactionType.VALIDATOR_FEE)
                .Link<VerifyDevFee>(x => x.TransactionType == TransactionType.DEV_FEE)
                .Link<VerifySignature>(x => x.TransactionType == TransactionType.PAYMENT)
                // TODO: Check for duplicate tx
                .Link<FetchSenderWallet>(x => x.TransactionType == TransactionType.PAYMENT)
                .Link<TakeBalanceFromSender>(x => x.TransactionType == TransactionType.PAYMENT)
                .Link<UpdateSenderWallet>(x => x.TransactionType == TransactionType.PAYMENT)
                .Link<FetchRecipientWallet>()
                .Link<AddBlockRewardToRecipient>(x => x.TransactionType == TransactionType.MINER_FEE)
                .Link<AddBalanceToRecipient>()
                .Link<UpdateRecipientWallet>();

            if (!txExecutor.ExecuteBatch(block.Pow.Transactions, out var res)) {
                logger.LogWarning($"AddBlock failed with: {res}");
                return false;
            }

            walletManager.UpdateWallets(txContext.Wallets.Select(x => x.Value).Where(x => x.Updated));
            blockchainRepository.UpdateWallets(txContext.LedgerWalletCache.Select(x => x.Value));

            mempoolManager.RemoveTransactions(block.Pow.Transactions.Where(x => x.TransactionType == TransactionType.PAYMENT));

            if (block.Pow.Height % Constant.EPOCH_LENGTH_BLOCKS == 0)
            {
                var epochStart = blockchainRepository.GetBlock(block.Pow.Height - Constant.EPOCH_LENGTH_BLOCKS + 1);
                NextEpoch(epochStart, block.Pow, chainState);
            }

            chainState.POW.Height = block.Pow.Height;
            chainState.POW.TotalWork += block.Pow.Difficulty.ToWork();
            chainState.POW.LastHash = block.Pow.GetHash();
        }

        chainState.POS.Height = block.Height;
        chainState.POS.LastHash = block.GetHash();

        blockchainRepository.Add(block, chainState);
        blockchainRepository.Commit();

        mempoolManager.RemoveTransactions(block.Transactions.Where(x => x.TransactionType == TransactionType.PAYMENT));

        if (broadcastBlock) {
            BlockBroadcast.Post(block);

            if (block.Pow is not null)
            {
                var vote = new Vote
                {
                    Height = block.Height,
                    Hash = block.GetHash()
                };

                vote.Sign(walletManager.GetNodeWallet().PrivateKey);

                VoteBroadcast.Post(vote);
            }
        }

        foreach (var wallet in txContext.Wallets.Select(x => x.Value).Where(x => x.Updated)) {
            WalletBroadcast.Post(wallet);
        }

        return true;
    }

    public bool AddBlocks(List<PowBlock> blocks)
    {
        using var _ = rwlock.EnterWriteLockEx();
        using var blockchainRepository = new BlockRepository(true);

        var chainState = blockchainRepository.GetChainState();
        var wallets = walletManager.GetWallets();

        var blockchainContext = new BlockchainContext()
        {
            LastBlocks = blockchainRepository.Tail(11),
            CurrentDifficulty = chainState.POW.CurrentDifficulty,
            NetworkTime = discoveryManager.GetNetworkTime()
        };

        var blockExecutor = Executor.Create<PowBlock, BlockchainContext>(blockchainContext)
            .Link<VerifyDifficulty>()
            .Link<VerifyNonce>()
            .Link<VerifyId>(x => x.Id > 0)
            .Link<VerifyParentHash>(x => x.Id > 0)
            .Link<VerifyTimestampPast>(x => x.Id > 0)
            .Link<VerifyTimestampFuture>(x => x.Id > 0);
        
        var txContext = new TransactionContext(blockchainRepository, wallets);

        var txExecutor = Executor.Create<Transaction, TransactionContext>(txContext)
            .Link<VerifyBlockReward>(x => x.TransactionType == TransactionType.MINER_FEE)
            .Link<VerifyValidatorReward>(x => x.TransactionType == TransactionType.VALIDATOR_FEE)
            .Link<VerifyDevFee>(x => x.TransactionType == TransactionType.DEV_FEE)
            .Link<VerifySignature>(x => x.TransactionType == TransactionType.PAYMENT)
            // TODO: Check for duplicate tx
            .Link<FetchSenderWallet>(x => x.TransactionType == TransactionType.PAYMENT)
            .Link<TakeBalanceFromSender>(x => x.TransactionType == TransactionType.PAYMENT)
            .Link<UpdateSenderWallet>(x => x.TransactionType == TransactionType.PAYMENT)
            .Link<FetchRecipientWallet>()
            .Link<AddBlockRewardToRecipient>(x => x.TransactionType == TransactionType.MINER_FEE)
            .Link<AddBalanceToRecipient>()
            .Link<UpdateRecipientWallet>();

        var epochStart = blockchainRepository.GetBlock(chainState.POW.Height - (chainState.POW.Height % Constant.EPOCH_LENGTH_BLOCKS) + 1);

        int progress = 0;
        foreach (var block in blocks)
        {
            if (!blockExecutor.Execute(block, out var result)) {
                logger.LogError(blockchainContext.Ex, $"AddBlock failed with: {result}");
                return false;
            }

            txContext.Fee = block.Transactions.Where(x => x.TransactionType == TransactionType.PAYMENT).Select(x => x.MaxFee).DefaultIfEmpty().Min();
            txContext.FeeTotal = (ulong)block.Transactions.Where(x => x.TransactionType == TransactionType.PAYMENT).Select(x => (long)x.MaxFee).DefaultIfEmpty().Sum();
            txContext.Timestamp = block.Timestamp;

            if (!txExecutor.ExecuteBatch(block.Transactions, out var txresult)) {
                logger.LogError(txContext.Ex, $"AddBlock failed with: {txresult}");
                return false;
            }

            logger.LogInformation($"Added block {block.Height}");

            if (block.Height % Constant.EPOCH_LENGTH_BLOCKS == 0) {
                NextEpoch(epochStart, block, chainState);
                blockchainContext.CurrentDifficulty = chainState.POW.CurrentDifficulty;
            }

            if (block.Height % Constant.EPOCH_LENGTH_BLOCKS == 1) {
                epochStart = block;
            }

            chainState.POW.Height = block.Height;
            chainState.POW.TotalWork += block.Difficulty.ToWork();

            blockchainContext.LastBlocks.Add(block);

            ChainObserver.ReportProgress(++progress, blocks.Count);
        }

        walletManager.UpdateWallets(txContext.Wallets.Select(x => x.Value).Where(x => x.Updated));

        blockchainRepository.UpdateWallets(txContext.LedgerWalletCache.Select(x => x.Value));
        blockchainRepository.Add(blocks, chainState);

        mempoolManager.RemoveTransactions(blocks.SelectMany(x => x.Transactions).Where(x => x.TransactionType == TransactionType.PAYMENT));

        blockchainRepository.Commit();

        foreach (var wallet in txContext.Wallets.Select(x => x.Value).Where(x => x.Updated)) {
            WalletBroadcast.Post(wallet);
        }

        return true;
    }

    public Blocktemplate GetBlocktemplate(Address wallet)
    {
        using var _ = rwlock.EnterReadLockEx();
        using var blockchainRepository = new BlockRepository(false);

        var chainState = blockchainRepository.GetChainState();
        var lastBlock = blockchainRepository.Last();

        var transactions = mempoolManager.GetTransactions();

        var rand = new Random();

        var timestamp = DateTimeOffset.Now.ToUnixTimeSeconds();

        var block = new PowBlock {
            Height = chainState.POW.Height + 1,
            ParentHash = lastBlock.GetHash(),
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
        using var blockchainRepository = new BlockRepository();

        var chainState = blockchainRepository.GetChainState();
        return chainState.POW.CurrentDifficulty;
    }

    public long GetCurrentHeight()
    {
        using var _ = rwlock.EnterReadLockEx();
        using var blockchainRepository = new BlockRepository();

        return blockchainRepository.Count();
    }

    public SHA256Hash GetLastBlockhash()
    {
        using var _ = rwlock.EnterReadLockEx();
        using var blockchainRepository = new BlockRepository();

        return blockchainRepository.Last().GetHash();
    }

    public ulong GetBalance(Address address)
    {
        using var _ = rwlock.EnterReadLockEx();
        using var blockchainRepository = new BlockRepository();

        return blockchainRepository.GetWallet(address)?.Balance ?? 0;
    }

    public List<Transaction> AddTransactionsToQueue(IList<Transaction> transactions, bool broadcast = true)
    {
        using var _ = rwlock.EnterReadLockEx();
        using var blockchainRepository = new BlockRepository();

        var context = new TransactionContext(blockchainRepository, mempoolManager);

        var executor = Executor.Create<Transaction, TransactionContext>(context)
            .Link<NotReward>()
            .Link<CheckMinFee>()
            .Link<VerifySignature>()
            // TODO: Check for duplicate tx
            .Link<FetchSenderWallet>()
            .Link<HasFunds>();

        var newTransactions = transactions.Where(tx => !mempoolManager.HasTransaction(tx));
        var valid = executor.Execute(newTransactions);

        mempoolManager.AddTransactions(valid, broadcast);
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

        var newDiff = (BigInteger)(chainState.POW.CurrentDifficulty.ToWork() * new BigRational(expected / (double)elapsed));
        chainState.POW.CurrentDifficulty = newDiff.ToDifficulty();

        logger.LogInformation($"Epoch {epochEnd.Id / 100 + 1}: difficulty {BigInteger.Log(newDiff, 2)}, target = {newDiff}");
    }

    public BigInteger GetTotalWork()
    {
        using var _ = rwlock.EnterReadLockEx();
        using var blockchainRepository = new BlockRepository();
        var chainState = blockchainRepository.GetChainState();
        return chainState.POW.TotalWork;
    }

    public ChainState GetChainState()
    {
        using var _ = rwlock.EnterReadLockEx();
        using var blockchainRepository = new BlockRepository();
        return blockchainRepository.GetChainState();
    }

    public List<PowBlock> GetLastBlocks(int count)
    {
        using var _ = rwlock.EnterReadLockEx();
        using var blockchainRepository = new BlockRepository();
        return blockchainRepository.Tail(count);
    }

    public List<PowBlock> GetLastBlocks(long start, int count)
    {
        using var _ = rwlock.EnterReadLockEx();
        using var blockchainRepository = new BlockRepository();
        return blockchainRepository.Tail(start, count);
    }

    public bool SetChain(List<PowBlock> blocks)
    {
        var sortedBlocks = blocks.Where(x => x.Id > 0)
            .OrderBy(x => x.Id)
            .ToList();

        using var _ = rwlock.EnterWriteLockEx();
        using var blockchainRepository = new BlockRepository(true);

        var chainState = blockchainRepository.GetChainState();

        var min = sortedBlocks.Min(x => x.Id);
        var max = chainState.POW.Height;

        var ledgerWallets = new Dictionary<string, LedgerWallet>();
        var wallets = walletManager.GetWallets();

        long progress = 0;
        ChainObserver.ReportProgress(progress, sortedBlocks.Count);

        for (long i = max; i >= min; i--)
        {
            var cBlock = blockchainRepository.GetBlock(i);

            if (cBlock == null)
            {
                continue;
            }

            /*var fee = cBlock.Transactions.DefaultIfEmpty().Select(x => x?.MaxFee ?? 0UL).Min();

            foreach (var tx in cBlock.Transactions) 
            {
                if(tx.PublicKey != null) {
                    var senderAddr = tx.PublicKey.Value.ToAddress();
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
                        sWallet.WalletTransactions.RemoveAll(x => x.Id == cBlock.Id);
                        sWallet.Updated = true;
                    }
                }

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
                    rWallet.WalletTransactions.RemoveAll(x => x.Id == cBlock.Id);
                    rWallet.Updated = true;
                }

                blockchainRepository.DeleteTransaction(tx.Id);
            }*/

            blockchainRepository.UpdateWallets(ledgerWallets.Values);
            blockchainRepository.Delete(cBlock.Id);

            chainState.POW.Height--;
            chainState.POW.TotalWork -= cBlock.Difficulty.ToWork();
            chainState.POW.CurrentDifficulty = cBlock.Difficulty;

            ChainObserver.ReportProgress(++progress, sortedBlocks.Count);
        }

        if (wallets.Values.Count > 0) {
            walletManager.RollbackWallets(wallets.Values.ToList(), min);
        }

        blockchainRepository.SaveState(chainState);
        blockchainRepository.Commit();

        var last = sortedBlocks.Last();

        progress = 0;
        ChainObserver.ReportProgress(progress, sortedBlocks.Count);

        if(!AddBlocks(sortedBlocks))
        {
            logger.LogError($"Set chain failed");
            return false;
        }

        // BlockBroadcast.Post(sortedBlocks.Last());

        foreach (var wallet in wallets.Select(x => x.Value).Where(x => x.Updated)) {
            WalletBroadcast.Post(wallet);
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
        using var blockchainRepository = new BlockRepository(true);

        blockchainRepository.AddVote(vote);

        return true;
    }

    public List<PowBlock> GetFrom(long id)
    {
        using var _ = rwlock.EnterReadLockEx();
        using var blockchainRepository = new BlockRepository();

        return blockchainRepository.GetFrom(id);
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
}
