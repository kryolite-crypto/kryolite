using Kryolite.Interface;
using Kryolite.Model;
using Microsoft.Extensions.Logging;

namespace Kryolite.Module.SmartContract;

public interface IContext
{
    Contract Contract { get; }
    Transaction Transaction { get; }
    View View { get; }
    Random Rand { get; }

    long Balance { get; set; } // TODO: not here
    string ReturnValue { get; set; }
    List<IEvent> Events { get; }
    List<string> EventData { get; }
    List<string> MethodParams { get; }
    List<Transaction> ScheduledCalls { get; }
}
