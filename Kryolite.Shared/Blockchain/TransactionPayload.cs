using MemoryPack;

namespace Kryolite.Shared;

[MemoryPackable]
public partial class TransactionPayload
{
    public ITransactionPayload? Payload { get; set; }
}

[MemoryPackable]
[MemoryPackUnion(0, typeof(NewContract))]
[MemoryPackUnion(1, typeof(CallMethod))]
public partial interface ITransactionPayload
{

}
