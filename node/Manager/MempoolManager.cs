using System.Threading.Tasks.Dataflow;
using Marccacoin.Shared;
using Microsoft.Extensions.Logging;

namespace Marccacoin;

public class MempoolManager : IMempoolManager
{
    private readonly ReaderWriterLockSlim rwlock = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);
    private readonly ILogger<MempoolManager> logger;
    private readonly BufferBlock<Transaction> TransactionBroadcast = new BufferBlock<Transaction>();
    private PriorityQueue<Transaction, ulong> MempoolQueue = new PriorityQueue<Transaction, ulong>();

    public MempoolManager(ILogger<MempoolManager> logger)
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void AddTransaction(Transaction transaction)
    {
        using var _ = rwlock.EnterWriteLockEx();

        if (MempoolQueue.Count >= Constant.MAX_MEMPOOL_TX) {
            MempoolQueue.EnqueueDequeue(transaction, transaction.MaxFee);
            return;
        }

        MempoolQueue.Enqueue(transaction, transaction.MaxFee);
    }

    public List<Transaction> GetTransactions()
    {
        using var _ = rwlock.EnterReadLockEx();
        return MempoolQueue.PeekTail(Constant.MAX_BLOCK_TX - 3).ToList();
    }

    public void RemoveTransactions(List<Transaction> transactions)
    {
        // TODO: Not efficient
        var hashes = transactions.Select(tx => BitConverter.ToString(tx.CalculateHash().Buffer)).ToHashSet();

        using var _ = rwlock.EnterWriteLockEx();
        var queue = MempoolQueue.UnorderedItems.Where(x => !hashes.Contains(BitConverter.ToString(x.Element.CalculateHash().Buffer)));

        MempoolQueue = new PriorityQueue<Transaction, ulong>(queue);
    }
}
