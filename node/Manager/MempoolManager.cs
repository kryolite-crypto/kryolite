using System.Threading.Tasks.Dataflow;
using Kryolite.Shared;
using Microsoft.Extensions.Logging;

namespace Kryolite.Node;

public class MempoolManager : IMempoolManager
{
    private readonly ReaderWriterLockSlim rwlock = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);
    private readonly ILogger<MempoolManager> logger;
    private readonly BroadcastBlock<Transaction> TransactionAddedBroadcast = new BroadcastBlock<Transaction>(x => x);
    private readonly BroadcastBlock<Transaction> TransactionRemovedBroadcast = new BroadcastBlock<Transaction>(x => x);

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
        TransactionAddedBroadcast.Post(transaction);
    }

    public void AddTransactions(List<Transaction> transactions, bool broadcast)
    {
        if (transactions.Count == 0) {
            return;
        }

        using var _ = rwlock.EnterWriteLockEx();
        foreach (var transaction in transactions) {
            Add(transaction);
            TransactionAddedBroadcast.Post(transaction);
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

    public void RemoveTransactions(IEnumerable<Transaction> transactions)
    {
        // TODO: Not efficient
        var hashes = transactions.Select(tx => BitConverter.ToString(tx.CalculateHash().Buffer)).ToHashSet();

        using var _ = rwlock.EnterWriteLockEx();

        // Filter removed transactions out and recreate queue
        var queue = MempoolQueue.UnorderedItems.Where(x => !hashes.Contains(BitConverter.ToString(x.Element.CalculateHash().Buffer)));
        MempoolQueue = new PriorityQueue<Transaction, ulong>(queue);

        foreach (var transaction in transactions) {
            var txHash = BitConverter.ToString(transaction.CalculateHash());
            PendingHashes.Remove(txHash);

            var address = transaction.PublicKey!.Value.ToAddress().ToString();
            if(PendingAmount.ContainsKey(address)) {
                PendingAmount[address] -= transaction.Value;

                if (PendingAmount[address] == 0) {
                    PendingAmount.Remove(address);
                }
            }

            TransactionRemovedBroadcast.Post(transaction);
        }
    }

    public IDisposable OnTransactionAdded(ITargetBlock<Transaction> action)
    {
        return TransactionAddedBroadcast.LinkTo(action);
    }

    public IDisposable OnTransactionRemoved(ITargetBlock<Transaction> action)
    {
        return TransactionRemovedBroadcast.LinkTo(action);
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

            TransactionRemovedBroadcast.Post(removed);

            return;
        }

        MempoolQueue.Enqueue(transaction, transaction.MaxFee);

        var from = transaction.PublicKey.Value.ToAddress().ToString();

        if(!PendingAmount.TryAdd(from, transaction.Value)) {
            PendingAmount[from] += transaction.Value;
        }

        PendingHashes.Add(BitConverter.ToString(transaction.CalculateHash()));
    }
}
