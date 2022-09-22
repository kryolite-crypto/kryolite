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
    private Dictionary<string, ulong> PendingAmount = new Dictionary<string, ulong>();
    private HashSet<string> PendingHashes = new HashSet<string>();

    public MempoolManager(ILogger<MempoolManager> logger)
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void AddTransaction(Transaction transaction)
    {
        using var _ = rwlock.EnterWriteLockEx();
        Add(transaction);
    }

    public void AddTransactions(List<Transaction> transactions)
    {
        using var _ = rwlock.EnterWriteLockEx();
        foreach (var transaction in transactions) {
            Add(transaction);
        }
    }

    public bool HasTransaction(Transaction transaction)
    {
        return PendingHashes.Contains(BitConverter.ToString(transaction.CalculateHash()));
    }

    public ulong GetPending(Address address)
    {
        return PendingAmount.GetValueOrDefault(address.ToString(), 0UL);
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

    private void Add(Transaction transaction)
    {
        if(transaction.PublicKey == null) {
            return;
        }

        if (MempoolQueue.Count >= Constant.MAX_MEMPOOL_TX) {
            var removed = MempoolQueue.EnqueueDequeue(transaction, transaction.MaxFee);
            
            string addr = removed.PublicKey!.Value.ToAddress().ToString();

            PendingAmount[addr] -= removed.Value;
            PendingHashes.Remove(BitConverter.ToString(removed.CalculateHash()));

            return;
        }

        MempoolQueue.Enqueue(transaction, transaction.MaxFee);

        var from = transaction.PublicKey.Value.ToAddress().ToString();

        if(!PendingAmount.TryAdd(from, transaction.Value)) {
            PendingAmount[from] = transaction.Value;
        }

        PendingHashes.Add(BitConverter.ToString(transaction.CalculateHash()));
    }
}
