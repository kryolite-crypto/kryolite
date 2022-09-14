using System.Numerics;
using System.Security.Cryptography;
using ExtendedNumerics;
using Marccacoin.Shared;
using Microsoft.Extensions.Logging;

namespace Marccacoin;

public class BlockchainManager : IBlockchainManager
{
    private readonly IDiscoveryManager discoveryManager;
    private readonly IBlockchainRepository blockchainRepository;
    private readonly ILogger<BlockchainManager> logger;
    private readonly ReaderWriterLockSlim rwlock = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);

    public BlockchainManager(IDiscoveryManager discoveryManager, IBlockchainRepository blockchainRepository, ILogger<BlockchainManager> logger)
    {
        this.discoveryManager = discoveryManager ?? throw new ArgumentNullException(nameof(discoveryManager));
        this.blockchainRepository = blockchainRepository ?? throw new ArgumentNullException(nameof(blockchainRepository));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool AddBlock(Block block)
    {
        using var _ = rwlock.EnterWriteLockEx();

        if (!VerifyBlock(block)) {
            return false;
        }

        var chainState = blockchainRepository.GetChainState();

        chainState.Height = block.Id;
        chainState.TotalWork += block.Header.Difficulty.ToWork();

        logger.LogInformation($"Added block {block.Id} (TotalWork={chainState.TotalWork.ToString()})");

        if (block.Id % Constant.EPOCH_LENGTH_BLOCKS == 0) {
            NextEpoch(chainState);
        }

        blockchainRepository.Add(block, chainState);
        
        return true;
    }

    public Blocktemplate GetBlocktemplate(Address wallet)
    {
        using var _ = rwlock.EnterReadLockEx();
        
        var chainState = blockchainRepository.GetChainState();
        var lastBlock = blockchainRepository.Last();

        var transactions = new List<Transaction>();

        var rand = new Random();

        transactions.Add(new Transaction {
            TransactionType = TransactionType.MINER_FEE,
            To = wallet,
            Value = (ulong)(10000000000000000000 * Constant.MINER_FEE),
            Nonce = rand.Next(int.MinValue, int.MaxValue)
        });

        transactions.Add(new Transaction {
            TransactionType = TransactionType.VALIDATOR_FEE,
            To = wallet,
            Value = (ulong)(10000000000000000000 * Constant.VALIDATOR_FEE),
            Nonce = rand.Next(int.MinValue, int.MaxValue)
        });

        transactions.Add(new Transaction {
            TransactionType = TransactionType.DEV_FEE,
            To = wallet,
            Value = (ulong)(10000000000000000000 * Constant.DEV_FEE),
            Nonce = rand.Next(int.MinValue, int.MaxValue)
        });

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
        var chainState = blockchainRepository.GetChainState();
        return chainState.CurrentDifficulty;
    }

    public long GetCurrentHeight()
    {
        using var _ = rwlock.EnterReadLockEx();
        return blockchainRepository.Count();
    }

    public SHA256Hash GetLastBlockhash()
    {    
        using var _ = rwlock.EnterReadLockEx();
        return blockchainRepository.Last().GetHash();
    }

    private bool VerifyBlock(Block block)
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

    private void NextEpoch(ChainState chainState)
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
}
