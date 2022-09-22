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
    private readonly IDiscoveryManager discoveryManager;
    private readonly IMempoolManager mempoolManager;
    private readonly ILogger<BlockchainManager> logger;
    private readonly ReaderWriterLockSlim rwlock = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);

    private BroadcastBlock<Block> BlockBroadcast = new BroadcastBlock<Block>(i => i);
    private BroadcastBlock<Wallet> WalletBroadcast = new BroadcastBlock<Wallet>(i => i);

    public BlockchainManager(IDiscoveryManager discoveryManager, IMempoolManager mempoolManager, ILogger<BlockchainManager> logger)
    {
        this.discoveryManager = discoveryManager ?? throw new ArgumentNullException(nameof(discoveryManager));
        this.mempoolManager = mempoolManager ?? throw new ArgumentNullException(nameof(mempoolManager));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Block GetBlock(long id)
    {
        using var _ = rwlock.EnterReadLockEx();
        using var blockchainRepository = new BlockRepository();

        return blockchainRepository.GetBlock(id);
    }

    public bool AddBlock(Block block)
    {
        using var _ = rwlock.EnterWriteLockEx();
        using var blockchainRepository = new BlockRepository(true);
        using var ledgerRepository = new LedgerRepository(true);
        using var walletRepository = new WalletRepository(true);

        if (!VerifyBlock(blockchainRepository, block)) {
            return false;
        }

        var chainState = blockchainRepository.GetChainState();

        chainState.Height = block.Id;
        chainState.TotalWork += block.Header.Difficulty.ToWork();

        var wallets = walletRepository.GetWallets();

        var context = new GlobalContext(ledgerRepository, wallets)
        {
            Fee = block.Transactions.DefaultIfEmpty().Min(x => x?.MaxFee ?? 0),
            FeeTotal = (ulong)block.Transactions.DefaultIfEmpty().Sum(x => (long)(x?.MaxFee ?? 0)),
            Timestamp = block.Header.Timestamp
        };

        var executor = Executor.Create<Transaction, GlobalContext>(context)
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

        if (!executor.ExecuteBatch(block.Transactions, out var result)) {
            logger.LogError(context.Ex, $"AddBlock failed with: {result}");
            return false;
        }

        walletRepository.UpdateWallets(context.Wallets.Select(x => x.Value).Where(x => x.Updated));
        ledgerRepository.UpdateWallets(context.LedgerWalletCache.Select(x => x.Value));

        logger.LogInformation($"Added block {block.Id} (TotalWork={chainState.TotalWork})");

        if (block.Id % Constant.EPOCH_LENGTH_BLOCKS == 0) {
            NextEpoch(blockchainRepository, chainState);
        }

        blockchainRepository.Add(block, chainState);

        ledgerRepository.Commit();
        blockchainRepository.Commit();
        walletRepository.Commit();

        mempoolManager.RemoveTransactions(block.Transactions);

        BlockBroadcast.Post(block);

        foreach (var wallet in context.Wallets.Select(x => x.Value).Where(x => x.Updated)) {
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
        using var ledgerRepository = new LedgerRepository();

        return ledgerRepository.GetWallet(address)?.Balance ?? 0;
    }

    public List<WalletTransaction> GetTransactions(int count)
    {
        using var _ = rwlock.EnterReadLockEx();
        using var walletRepository = new WalletRepository(false);

        return walletRepository.GetLastTransactions(count);
    }

    public Wallet CreateWallet()
    {
        using var _ = rwlock.EnterWriteLockEx();
        using var walletRepository = new WalletRepository(true);

        var wallet = new Wallet();
        walletRepository.Add(wallet);
        walletRepository.Commit();

        return wallet;
    }

    public List<Wallet> GetWallets()
    {
        using var _ = rwlock.EnterReadLockEx();
        using var walletRepository = new WalletRepository();

        return walletRepository.GetWallets().Values.ToList();
    }

    /**
        Only allowed tp update Wallet description
    **/
    public void UpdateWallet(Wallet wal)
    {
        using var _ = rwlock.EnterWriteLockEx();
        using var walletRepository = new WalletRepository(false);

        var wallet = walletRepository.Get(wal.Address);
        wallet.Description = wal.Description;

        walletRepository.Update(wallet);
    }

    public void AddTransactionsToQueue(List<Transaction> transactions)
    {
        using var _ = rwlock.EnterReadLockEx();
        using var ledgerRepository = new LedgerRepository();

        var context = new GlobalContext(ledgerRepository, mempoolManager);

        var executor = Executor.Create<Transaction, GlobalContext>(context)
            .Link<NotReward>()
            .Link<CheckMinFee>()
            .Link<VerifySignature>()
            // TODO: Check for duplicate tx
            .Link<FetchSenderWallet>()
            .Link<HasFunds>();

        var newTransactions = transactions.Where(tx => !mempoolManager.HasTransaction(tx));
        var valid = executor.Execute(newTransactions);

        mempoolManager.AddTransactions(valid);
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

    private void NextEpoch(BlockRepository blockchainRepository, ChainState chainState)
    {
        var blockCount = blockchainRepository.Count();
        if (blockCount == 0) {
            // Starting difficulty
            chainState.CurrentDifficulty = new Difficulty { b0 = Constant.STARTING_DIFFICULTY };
            return;
        }

        var epochEnd = blockchainRepository.Last();
        var epochStart = blockchainRepository.GetBlock(Math.Max(1, epochEnd.Id - Constant.EPOCH_LENGTH_BLOCKS));

        var elapsed = epochEnd.Header.Timestamp - epochStart.Header.Timestamp;
        var expected = Constant.TARGET_BLOCK_TIME_S * Constant.EPOCH_LENGTH_BLOCKS;

        var newDiff = BigRational.Multiply(chainState.CurrentDifficulty.ToWork(), new BigRational(expected / (decimal)elapsed)).WholePart;
        chainState.CurrentDifficulty = newDiff.ToDifficulty();

        logger.LogInformation($"Epoch {epochEnd.Id / 100 + 1}: difficulty {BigInteger.Log(newDiff, 2)}, target = {newDiff}");
    }

    public IDisposable OnBlockAdded(ActionBlock<Block> action)
    {
        return BlockBroadcast.LinkTo(action);
    }

    public IDisposable OnWalletUpdated(ActionBlock<Wallet> action)
    {
        return WalletBroadcast.LinkTo(action);
    }
}
