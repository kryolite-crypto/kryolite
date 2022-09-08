using System.Numerics;
using ExtendedNumerics;
using Microsoft.Extensions.Logging;

namespace Marccacoin;

public class BlockchainManager : IBlockchainManager
{
    private BigInteger TotalWork { get; set; } = new BigInteger(0);
    private Difficulty CurrentDifficulty { get; set; } = new Difficulty { Value = 0 };
    private List<Block> Blocks = new List<Block>();
    private readonly IDiscoveryManager discoveryManager;
    private readonly ILogger<BlockchainManager> logger;
    private readonly ReaderWriterLockSlim rwlock = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);

    public BlockchainManager(IDiscoveryManager discoveryManager, ILogger<BlockchainManager> logger)
    {
        this.discoveryManager = discoveryManager ?? throw new ArgumentNullException(nameof(discoveryManager));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool AddBlock(Block block)
    {
        using var _ = rwlock.EnterWriteLockEx();

        if (!VerifyBlock(block)) {
            return false;
        }

        Blocks.Add(block);

        TotalWork += block.Header.Difficulty.ToWork();

        logger.LogInformation($"Added block {block.Header.Id} (chain TotalWork={TotalWork.ToString()})");

        if (block.Header.Id % (ulong)Constant.EPOCH_LENGTH_BLOCKS == 0) {
            NextEpoch();
        }
        
        return true;
    }

    public Difficulty GetCurrentDifficulty()
    {
        using var _ = rwlock.EnterReadLockEx();
        return CurrentDifficulty;
    }

    public ulong GetCurrentHeight()
    {
        using var _ = rwlock.EnterReadLockEx();
        return (ulong)Blocks.Count;
    }

    public SHA256Hash GetLastBlockhash()
    {    
        using var _ = rwlock.EnterReadLockEx();
        return Blocks.Last().Header.GetHash();
    }

    private bool VerifyBlock(Block block)
    {
        if (block.Header.Difficulty != CurrentDifficulty) {
            Console.WriteLine("diff");
            return false;
        }

        if (block.Header.Id != (ulong)Blocks.Count) {
            Console.WriteLine("id");
            return false;
        }

        if (!block.Header.VerifyNonce()) {
            Console.WriteLine("nonce");
            return false;
        }

        if (Blocks.Count > 0) {
            var lastBlock = Blocks.Last();

            if (block.Header.ParentHash != lastBlock.Header.GetHash()) {
                return false;
            }

            // Get median of last 11 blocks
            var median = Blocks.Skip(Math.Max(0, Blocks.Count() - 11))
                .ElementAt(Math.Min(Blocks.Count / 2, 5));

            if (block.Header.Timestamp < median.Header.Timestamp) {
                return false;
            }

            // Timestamp must be within 2 hours of average network time
            if (block.Header.Timestamp > discoveryManager.GetNetworkTime().AddHours(2).ToUnixTimeSeconds()) {
                return false;
            }

            // TODO: check max transactions
        }

        return true;
    }

    private void NextEpoch()
    {
        if (Blocks.Count == 1) {
            // Starting difficulty
            CurrentDifficulty = new Difficulty {
                b0 = 20,
                b1 = 0,
                b2 = 0,
                b3 = 0
            };

            return;
        }

        var epochStart = Blocks.ElementAt(new Index(Constant.EPOCH_LENGTH_BLOCKS, true)); // TODO: instead get last 100 blocks from db
        var epochEnd = Blocks.Last();

        var elapsed = epochEnd.Header.Timestamp - epochStart.Header.Timestamp;
        var expected = Constant.TARGET_BLOCK_TIME_S * Constant.EPOCH_LENGTH_BLOCKS;

        var newDiff = BigRational.Multiply(CurrentDifficulty.ToWork(), new BigRational(expected / (decimal)elapsed)).WholePart;
        CurrentDifficulty = newDiff.ToDifficulty();

        logger.LogInformation($"Epoch {epochEnd.Header.Id / 100 + 1}: difficulty {BigInteger.Log(newDiff, 2)}, target = {newDiff}");
    }
}
