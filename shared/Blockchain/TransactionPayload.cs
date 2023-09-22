using MessagePack;
using MessagePack.Formatters;

namespace Kryolite.Shared;

[MessagePackObject]
public class TransactionPayload
{
    [Key(0)]
    public ITransactionPayload? Payload { get; set; }
}

[Union(0, typeof(NewContract))]
[Union(1, typeof(CallMethod))]
public interface ITransactionPayload
{

}
