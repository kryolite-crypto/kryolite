using System.Numerics;
using System.Security.Cryptography;
using System.Threading.Tasks.Dataflow;
using ExtendedNumerics;
using Marccacoin.Shared;
using Microsoft.Extensions.Logging;
using NSec.Cryptography;

namespace Marccacoin;

public class BlockchainManager : IBlockchainManager
{
    private readonly INetworkManager discoveryManager;
    private readonly IMempoolManager mempoolManager;
    private readonly IWalletManager walletManager;
    private readonly ILogger<BlockchainManager> logger;
    private readonly ReaderWriterLockSlim rwlock = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);

    private BroadcastBlock<Block> BlockBroadcast = new BroadcastBlock<Block>(i => i);
    private BroadcastBlock<Wallet> WalletBroadcast = new BroadcastBlock<Wallet>(i => i);

    public BlockchainManager(INetworkManager discoveryManager, IMempoolManager mempoolManager, IWalletManager walletManager, ILogger<BlockchainManager> logger)
    {
        this.discoveryManager = discoveryManager ?? throw new ArgumentNullException(nameof(discoveryManager));
        this.mempoolManager = mempoolManager ?? throw new ArgumentNullException(nameof(mempoolManager));
        this.walletManager = walletManager ?? throw new ArgumentNullException(nameof(walletManager));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Block? GetBlock(long id)
    {
        using var _ = rwlock.EnterReadLockEx();
        using var blockchainRepository = new BlockRepository();

        return blockchainRepository.GetBlock(id);
    }

    public bool AddBlock(Block block, bool broadcastBlock = true)
    {
        using var _ = rwlock.EnterWriteLockEx();
        using var blockchainRepository = new BlockRepository(true);

        var chainState = blockchainRepository.GetChainState();

        var blockchainContext = new BlockchainContext()
        {
            LastBlocks = blockchainRepository.Tail(11),
            CurrentDifficulty = chainState.CurrentDifficulty,
            NetworkTime = discoveryManager.GetNetworkTime()
        };

        var blockExecutor = Executor.Create<Block, BlockchainContext>(blockchainContext)
            .Link<VerifyDifficulty>()
            .Link<VerifyNonce>()
            .Link<VerifyId>(x => x.Id > 0)
            .Link<VerifyParentHash>(x => x.Id > 0)
            .Link<VerifyTimestampPast>(x => x.Id > 0)
            .Link<VerifyTimestampFuture>(x => x.Id > 0);

        if (!blockExecutor.Execute(block, out var result)) {
            logger.LogError(blockchainContext.Ex, $"AddBlock failed with: {result}");
            return false;
        }

        var wallets = walletManager.GetWallets();

        var txContext = new TransactionContext(blockchainRepository, wallets)
        {
            Fee = block.Transactions.Where(x => x.TransactionType == TransactionType.PAYMENT).Select(x => x.MaxFee).DefaultIfEmpty().Min(),
            FeeTotal = (ulong)block.Transactions.Where(x => x.TransactionType == TransactionType.PAYMENT).Select(x => (long)x.MaxFee).DefaultIfEmpty().Sum(),
            Timestamp = block.Header.Timestamp
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
            logger.LogError(txContext.Ex, $"AddBlock failed with: {txresult}");
            return false;
        }

        walletManager.UpdateWallets(txContext.Wallets.Select(x => x.Value).Where(x => x.Updated));
        blockchainRepository.UpdateWallets(txContext.LedgerWalletCache.Select(x => x.Value));

        logger.LogInformation($"Added block {block.Id}");

        if (block.Id % Constant.EPOCH_LENGTH_BLOCKS == 0) {
            var epochStart = blockchainRepository.GetBlock(block!.Id - Constant.EPOCH_LENGTH_BLOCKS + 1);
            NextEpoch(epochStart, block, chainState);
        }

        chainState.Height = block.Id;
        chainState.TotalWork += block.Header.Difficulty.ToWork();

        blockchainRepository.Add(block, chainState);

        blockchainRepository.Commit();

        mempoolManager.RemoveTransactions(block.Transactions.Where(x => x.TransactionType == TransactionType.PAYMENT));

        if (broadcastBlock) {
            BlockBroadcast.Post(block);
        }

        foreach (var wallet in txContext.Wallets.Select(x => x.Value).Where(x => x.Updated)) {
            WalletBroadcast.Post(wallet);
        }

        return true;
    }

    public bool AddBlocks(List<Block> blocks)
    {
        using var _ = rwlock.EnterWriteLockEx();
        using var blockchainRepository = new BlockRepository(true);

        var chainState = blockchainRepository.GetChainState();
        var wallets = walletManager.GetWallets();

        var blockchainContext = new BlockchainContext()
        {
            LastBlocks = blockchainRepository.Tail(11),
            CurrentDifficulty = chainState.CurrentDifficulty,
            NetworkTime = discoveryManager.GetNetworkTime()
        };

        var blockExecutor = Executor.Create<Block, BlockchainContext>(blockchainContext)
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

        var epochStart = blockchainRepository.GetBlock(chainState.Height - (chainState.Height % Constant.EPOCH_LENGTH_BLOCKS) + 1);
        Console.WriteLine(chainState.Height - (chainState.Height % Constant.EPOCH_LENGTH_BLOCKS) + 1);

        int progress = 0;
        foreach (var block in blocks)
        {
            if (!blockExecutor.Execute(block, out var result)) {
                logger.LogError(blockchainContext.Ex, $"AddBlock failed with: {result}");
                return false;
            }

            txContext.Fee = block.Transactions.Where(x => x.TransactionType == TransactionType.PAYMENT).Select(x => x.MaxFee).DefaultIfEmpty().Min();
            txContext.FeeTotal = (ulong)block.Transactions.Where(x => x.TransactionType == TransactionType.PAYMENT).Select(x => (long)x.MaxFee).DefaultIfEmpty().Sum();
            txContext.Timestamp = block.Header.Timestamp;

            if (!txExecutor.ExecuteBatch(block.Transactions, out var txresult)) {
                logger.LogError(txContext.Ex, $"AddBlock failed with: {txresult}");
                return false;
            }

            logger.LogInformation($"Added block {block.Id}");

            if (block.Id % Constant.EPOCH_LENGTH_BLOCKS == 0) {
                NextEpoch(epochStart, block, chainState);
                blockchainContext.CurrentDifficulty = chainState.CurrentDifficulty;
            }

            if (block.Id % Constant.EPOCH_LENGTH_BLOCKS == 1) {
                epochStart = block;
            }

            chainState.Height = block.Id;
            chainState.TotalWork += block.Header.Difficulty.ToWork();

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

        var transactions = new List<Transaction>();

        var rand = new Random();

        transactions.Add(new Transaction {
            TransactionType = TransactionType.MINER_FEE,
            To = wallet,
            Value = (ulong)(1000000000 * Constant.MINER_FEE),
            Nonce = rand.Next(int.MinValue, int.MaxValue)
        });

        transactions.Add(new Transaction {
            TransactionType = TransactionType.VALIDATOR_FEE,
            To = "FIM0xA101CFBF69818C624A03AF8C8FDD9B345896EE1215287EABA4CB",
            Value = (ulong)(1000000000 * Constant.VALIDATOR_FEE),
            Nonce = rand.Next(int.MinValue, int.MaxValue)
        });

        transactions.Add(new Transaction {
            TransactionType = TransactionType.DEV_FEE,
            To = "FIM0xA10192865B85F23DC1E7B13A486DBDAFBE0CFF3F4A0CB83561C0",
            Value = (ulong)(1000000000 * Constant.DEV_FEE),
            Nonce = rand.Next(int.MinValue, int.MaxValue)
        });

        transactions.AddRange(mempoolManager.GetTransactions());

        var timestamp = DateTimeOffset.Now.ToUnixTimeSeconds();

        var block = new Block {
            Id = chainState.Height + 1,
            Header = new BlockHeader {
                ParentHash = lastBlock.GetHash(),
                Timestamp = new DateTimeOffset(DateTime.Now).ToUnixTimeSeconds(),
                Difficulty = chainState.CurrentDifficulty
            },
            Transactions = transactions
        };

        return new Blocktemplate
        {
            Id = block.Id,
            Difficulty = chainState.CurrentDifficulty,
            ParentHash = block.Header.ParentHash,
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
        return chainState.CurrentDifficulty;
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

    public List<Transaction> AddTransactionsToQueue(List<Transaction> transactions, bool broadcast = true)
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

    private bool VerifyBlock(BlockRepository blockchainRepository, Block block)
    {
        var blockCount = blockchainRepository.Count();
        var chainState = blockchainRepository.GetChainState();

        if (block.Header.Difficulty != chainState.CurrentDifficulty) {
            Console.WriteLine("diff");
            return false;
        }

        if (!block.VerifyNonce()) {
            Console.WriteLine("nonce");
            return false;
        }

        if (blockCount > 0) {
            var lastBlock = blockchainRepository.Last();

            if (block.Id != lastBlock.Id + 1) {
                Console.WriteLine("id");
                return false;
            }

            if (!Enumerable.SequenceEqual((byte[])block.Header.ParentHash, (byte[])lastBlock.GetHash())) {
                Console.WriteLine("last_hash");
                return false;
            }

            // Get median of last 11 blocks
            var median = blockchainRepository.Tail(11)
                .ElementAt((int)(Math.Min(blockCount / 2, 5)));

            if (block.Header.Timestamp < median.Header.Timestamp) {
                Console.WriteLine("too old");
                return false;
            }

            // Timestamp must be within 2 hours of average network time
            if (block.Header.Timestamp > discoveryManager.GetNetworkTime().AddHours(2).ToUnixTimeSeconds()) {
                Console.WriteLine("in future");
                return false;
            }

            // TODO: check max transactions
        }

        return true;
    }

    private void NextEpoch(Block? epochStart, Block epochEnd, ChainState chainState)
    {
        if (chainState.Height == 0) {
            // Starting difficulty
            chainState.CurrentDifficulty = new Difficulty { b0 = Constant.STARTING_DIFFICULTY };
            return;
        }

        if (epochStart == null) {
            throw new Exception("Epoch Start block not found");
        }

        var elapsed = epochEnd.Header.Timestamp - epochStart.Header.Timestamp;
        var expected = Constant.TARGET_BLOCK_TIME_S * Constant.EPOCH_LENGTH_BLOCKS;

        var newDiff = BigRational.Multiply(chainState.CurrentDifficulty.ToWork(), new BigRational(expected / (decimal)elapsed)).WholePart;
        chainState.CurrentDifficulty = newDiff.ToDifficulty();

        logger.LogInformation($"Epoch {epochEnd.Id / 100 + 1}: difficulty {BigInteger.Log(newDiff, 2)}, target = {newDiff}");
    }

    public IDisposable OnBlockAdded(ITargetBlock<Block> action)
    {
        return BlockBroadcast.LinkTo(action);
    }

    public IDisposable OnWalletUpdated(ITargetBlock<Wallet> action)
    {
        return WalletBroadcast.LinkTo(action);
    }

    public BigInteger GetTotalWork()
    {
        using var _ = rwlock.EnterReadLockEx();
        using var blockchainRepository = new BlockRepository();
        var chainState = blockchainRepository.GetChainState();
        return chainState.TotalWork;
    }

    public ChainState GetChainState()
    {
        using var _ = rwlock.EnterReadLockEx();
        using var blockchainRepository = new BlockRepository();
        return blockchainRepository.GetChainState();
    }

    public List<Block> GetLastBlocks(int count)
    {
        using var _ = rwlock.EnterReadLockEx();
        using var blockchainRepository = new BlockRepository();
        return blockchainRepository.Tail(count);
    }

    public List<Block> GetLastBlocks(long start, int count)
    {
        using var _ = rwlock.EnterReadLockEx();
        using var blockchainRepository = new BlockRepository();
        return blockchainRepository.Tail(start, count);
    }

    public bool SetChain(List<Block> blocks)
    {
        var sortedBlocks = blocks.Where(x => x.Id > 0)
            .OrderBy(x => x.Id)
            .ToList();

        using var _ = rwlock.EnterWriteLockEx();
        using var blockchainRepository = new BlockRepository(true);

        var chainState = blockchainRepository.GetChainState();

        var min = sortedBlocks.Min(x => x.Id);
        var max = chainState.Height;

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

            var fee = cBlock.Transactions.DefaultIfEmpty().Select(x => x?.MaxFee ?? 0UL).Min();

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
                }

                blockchainRepository.DeleteTransaction(tx.Id);
            }

            blockchainRepository.UpdateWallets(ledgerWallets.Values);

            if (wallets.Values.Count > 0) {
                walletManager.RollbackWallets(wallets.Values.ToList(), min);
            }

            blockchainRepository.Delete(cBlock.Id);
            blockchainRepository.DeleteHeader(cBlock.Header.Id);

            chainState.Height--;
            chainState.TotalWork -= cBlock.Header.Difficulty.ToWork();
            chainState.CurrentDifficulty = cBlock.Header.Difficulty;

            ChainObserver.ReportProgress(++progress, sortedBlocks.Count);
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

        logger.LogInformation("Chain synchronization completed");
        return true;
    }

    public List<Block> GetFrom(long id)
    {
        using var _ = rwlock.EnterReadLockEx();
        using var blockchainRepository = new BlockRepository();

        return blockchainRepository.GetFrom(id);
    }
}
