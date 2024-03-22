using System.Threading.Tasks.Dataflow;

namespace Kryolite.Node.Network;

/// <summary>
/// Provides a dataflow block that batches inputs into arrays.
/// A batch is produced when the number of currently queued items becomes equal
/// to BatchSize, or when a Timeout period has elapsed after receiving the last item.
/// </summary>
public class BatchUntilInactiveBlock<T> : IPropagatorBlock<T, T[]>, IReceivableSourceBlock<T[]>
{
    private readonly BatchBlock<T> _source;
    private readonly Timer _timer;
    private readonly TimeSpan _timeout;

    public BatchUntilInactiveBlock(int batchSize, TimeSpan timeout, GroupingDataflowBlockOptions dataflowBlockOptions)
    {
        _source = new BatchBlock<T>(batchSize, dataflowBlockOptions);
        _timer = new Timer(_ => _source.TriggerBatch());
        _timeout = timeout;
    }

    public BatchUntilInactiveBlock(int batchSize, TimeSpan timeout) : this(batchSize,
        timeout, new GroupingDataflowBlockOptions())
    { }

    public int BatchSize => _source.BatchSize;
    public TimeSpan Timeout => _timeout;
    public Task Completion => _source.Completion;
    public int OutputCount => _source.OutputCount;

    public void Complete() => _source.Complete();

    void IDataflowBlock.Fault(Exception exception)
        => ((IDataflowBlock)_source).Fault(exception);

    public IDisposable LinkTo(ITargetBlock<T[]> target,
        DataflowLinkOptions linkOptions)
            => _source.LinkTo(target, linkOptions);

    public void TriggerBatch() => _source.TriggerBatch();

    public bool TryReceive(Predicate<T[]>? filter, out T[] item)
        => _source.TryReceive(filter, out item!);

    public bool TryReceiveAll(out IList<T[]> items)
        => _source.TryReceiveAll(out items!);

    DataflowMessageStatus ITargetBlock<T>.OfferMessage(
        DataflowMessageHeader messageHeader, T messageValue, ISourceBlock<T>? source,
        bool consumeToAccept)
    {
        var offerResult = ((ITargetBlock<T>)_source).OfferMessage(messageHeader,
            messageValue, source, consumeToAccept);
        if (offerResult == DataflowMessageStatus.Accepted)
            _timer.Change(_timeout, System.Threading.Timeout.InfiniteTimeSpan);
        return offerResult;
    }

    T[] ISourceBlock<T[]>.ConsumeMessage(DataflowMessageHeader messageHeader,
        ITargetBlock<T[]> target, out bool messageConsumed)
            => ((ISourceBlock<T[]>)_source).ConsumeMessage(messageHeader,
                target, out messageConsumed)!;

    bool ISourceBlock<T[]>.ReserveMessage(DataflowMessageHeader messageHeader,
        ITargetBlock<T[]> target)
            => ((ISourceBlock<T[]>)_source).ReserveMessage(messageHeader, target);

    void ISourceBlock<T[]>.ReleaseReservation(DataflowMessageHeader messageHeader,
        ITargetBlock<T[]> target)
            => ((ISourceBlock<T[]>)_source).ReleaseReservation(messageHeader, target);
}
