using System.Numerics;
using ExtendedNumerics;
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

        chainState.Height = block.Header.Id;
        chainState.TotalWork += block.Header.Difficulty.ToWork();

        logger.LogInformation($"Added block {block.Header.Id} (TotalWork={chainState.TotalWork.ToString()})");

        if (block.Header.Id % Constant.EPOCH_LENGTH_BLOCKS == 0) {
            NextEpoch(chainState);
        }

        blockchainRepository.Add(block, chainState);
        
        return true;
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
        return blockchainRepository.Last().Header.GetHash();
    }

    private bool VerifyBlock(Block block)
    {
        var blockCount = blockchainRepository.Count();
        var chainState = blockchainRepository.GetChainState();

        if (block.Header.Difficulty != chainState.CurrentDifficulty) {
            Console.WriteLine("diff");
            return false;
        }

        if (block.Header.Id != blockchainRepository.Count()) {
            Console.WriteLine("id");
            return false;
        }

        if (!block.Header.VerifyNonce()) {
            Console.WriteLine("nonce");
            return false;
        }

        if (blockCount > 0) {
            var lastBlock = blockchainRepository.Last();

            if (block.Header.ParentHash != lastBlock.Header.GetHash()) {
                return false;
            }

            // Get median of last 11 blocks
            var median = blockchainRepository.Tail(11)
                .ElementAt((int)(Math.Min(blockCount / 2, 5)));

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

    private void NextEpoch(ChainState chainState)
    {
        var blockCount = blockchainRepository.Count();
        if (blockCount == 0) {
            // Starting difficulty
            chainState.CurrentDifficulty = new Difficulty { b0 = Constant.STARTING_DIFFICULTY };
            return;
        }

        var epochEnd = blockchainRepository.Last();
        var epochStart = blockchainRepository.GetBlock(Math.Max(1, epochEnd.Header.Id - Constant.EPOCH_LENGTH_BLOCKS));

        var elapsed = epochEnd.Header.Timestamp - epochStart.Header.Timestamp;
        var expected = Constant.TARGET_BLOCK_TIME_S * Constant.EPOCH_LENGTH_BLOCKS;

        var newDiff = BigRational.Multiply(chainState.CurrentDifficulty.ToWork(), new BigRational(expected / (decimal)elapsed)).WholePart;
        chainState.CurrentDifficulty = newDiff.ToDifficulty();

        logger.LogInformation($"Epoch {epochEnd.Header.Id / 100 + 1}: difficulty {BigInteger.Log(newDiff, 2)}, target = {newDiff}");
    }
}
