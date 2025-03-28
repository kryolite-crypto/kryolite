using System.Security.Cryptography;
using System.Text;
using Kryolite.FastSerializer;
using Kryolite.Model;
using Microsoft.Extensions.Logging;
using Wasmtime;

namespace Kryolite.Module.SmartContract.ABI;

public class ApiV1 : IStandardApi
{
    public void Register(Linker linker, Store store)
    {
        linker.DefineFunction("env", "__println", _println);
        linker.DefineFunction("env", "__return", _return);
        linker.DefineFunction("env", "__rand", _rand);
        linker.DefineFunction("env", "__exit", _exit);
        linker.DefineFunction("env", "__transfer", _transfer);
        linker.DefineFunction("env", "__approval", _approval);
        linker.DefineFunction("env", "__transfer_token", _transferToken);
        linker.DefineFunction("env", "__consume_token", _transferToken);
        linker.DefineFunction("env", "__append_event", _appendEvent);
        linker.DefineFunction("env", "__publish_event", _publishEvent);
        linker.DefineFunction("env", "__hash_data", _hashData);
        linker.DefineFunction("env", "__schedule_param", _scheduleParam);
        linker.DefineFunction("env", "__schedule", _schedule);

        WASIShims(linker, store);
    }

    private CallerAction<int, int> _println = (Caller caller, int ptr, int len) =>
    {
        var memory = caller.GetMemory();
        var context = caller.GetContext();

        var msg = memory.ReadString(ptr, len, Encoding.UTF8);
        // context.Logger.LogDebug("LOG {contract}: {message}", context.Contract.Name, msg);
    };

    private CallerAction<int, int> _return = (Caller caller, int ptr, int len) =>
    {
        var memory = caller.GetMemory();
        var context = caller.GetContext();

        context.ReturnValue = memory.ReadString(ptr, len, Encoding.UTF8);
    };

    private CallerFunc<int> _rand = (Caller caller) =>
    {
        var context = caller.GetContext();
        return context.Rand.Next();
    };

    private CallerAction<int> _exit = (Caller caller, int exitCode) =>
    {
        throw new ExitException(exitCode);
    };

    private CallerAction<int, long> _transfer = (Caller caller, int address, long value) =>
    {
        if (value < 0)
        {
            throw new Exception("__transfer: negative value");
        }

        var memory = caller.GetMemory();
        var addr = memory.ReadAddress(address);

        if (addr is null)
        {
            throw new ExitException(101);
        }

        var context = caller.GetContext();
        var balance = checked(context.Balance - value);

        if (balance < 0)
        {
            throw new ExitException(102);
        }

        context.Balance = balance;
        context.Transaction.Effects.Add(new Effect(context.Contract.Address, context.Contract.Address, addr, (ulong)value));
    };

    private CallerAction<int, int, int> _approval = (Caller caller, int fromPtr, int toPtr, int tokenIdPtr) =>
    {
        var memory = caller.GetMemory();
        var context = caller.GetContext();

        var eventData = new ApprovalEventArgs
        {
            Contract = context.Contract.Address,
            From = memory.ReadAddress(fromPtr) ?? throw new Exception("__approval: null 'from' address"),
            To = memory.ReadAddress(toPtr) ?? throw new Exception("__approval: null 'to' address"),
            TokenId = memory.ReadU256(tokenIdPtr) ?? throw new Exception("__approval: null 'tokenIdPtr' address")
        };

        context.Events.Add(eventData);
    };

    private CallerAction<int, int, int, int, int, int, int> _transferToken = (Caller caller, int fromPtr, int toPtr, int tokenIdPtr, int namePtr, int nameLen, int descPtr, int descLen) =>
    {
        var memory = caller.GetMemory();
        var context = caller.GetContext();

        var tokenId = memory.ReadU256(tokenIdPtr);
        var from = memory.ReadAddress(fromPtr);
        var to = memory.ReadAddress(toPtr);
        var name = memory.ReadString(namePtr, nameLen, Encoding.UTF8);
        var desc = memory.ReadString(descPtr, descLen, Encoding.UTF8);

        /*if (context.Logger.IsEnabled(LogLevel.Debug))
        {
            context.Logger.LogDebug("Transfer token {id} ({name} - {desc}) from {from} to {to}", tokenId, name, desc, from, to);
        }*/

        var eventData = new TransferTokenEventArgs
        {
            Contract = context.Contract.Address,
            From = from,
            To = to,
            TokenId = tokenId
        };

        var effect = new Effect(context.Contract.Address, from, to, 0, tokenId)
        {
            Name = name,
            Description = desc
        };

        context.Events.Add(eventData);
        context.Transaction.Effects.Add(effect);
    };

    private CallerAction<int, int> _consumeToken = (Caller caller, int ownerPtr, int tokenIdPtr) =>
    {
        var memory = caller.GetMemory();
        var context = caller.GetContext();

        var eventData = new ConsumeTokenEventArgs
        {
            Contract = context.Contract.Address,
            Owner = memory.ReadAddress(ownerPtr) ?? throw new Exception("__consume_token: null 'tokenIdPtr' address"),
            TokenId = memory.ReadU256(tokenIdPtr) ?? throw new Exception("__consume_token: null 'tokenIdPtr' address")
        };

        context.Events.Add(eventData);
        context.Transaction.Effects.Add(new Effect(context.Contract.Address, context.Contract.Address, eventData.Owner, 0, eventData.TokenId, true));
    };

    private CallerAction<int, int> _appendEvent = (Caller caller, int ptr, int len) =>
    {
        var memory = caller.GetMemory();
        var context = caller.GetContext();

        var msg = memory.ReadString(ptr, len, Encoding.UTF8);
        context.EventData.Add(msg);
    };

    private CallerAction _publishEvent = (Caller caller) =>
    {
        var context = caller.GetContext();
        var eventData = new GenericEventArgs
        {
            Contract = context.Contract.Address
        };

        eventData.EventData.AddRange(context.EventData);

        context.Events.Add(eventData);
        context.EventData.Clear();
    };

    private CallerAction<int, int, int, int> _hashData = (Caller caller, int srcPtr, int srcLen, int destPtr, int destLen) =>
    {
        var memory = caller.GetMemory();

        var src = memory.GetSpan<byte>(srcPtr, srcLen);
        var dest = memory.GetSpan<byte>(destPtr, destLen);

        if (!SHA256.TryHashData(src, dest, out _))
        {
            throw new Exception("hash failed");
        }
    };

    private CallerAction<int, int> _scheduleParam = (Caller caller, int ptr, int len) =>
    {
        var memory = caller.GetMemory();
        var context = caller.GetContext();

        var data = memory.GetSpan(ptr, len);

        context.MethodParams.Add(Encoding.UTF8.GetString(data));
    };

    private CallerAction<int, int, long, int> _schedule = (Caller caller, int ptr, int len, long timestamp, int maxFee) =>
    {
        var memory = caller.GetMemory();
        var context = caller.GetContext();

        var method = memory.ReadString(ptr, len, Encoding.UTF8);
        var payload = new TransactionPayload
        {
            Payload = new CallMethod
            {
                Method = method,
                Params = context.MethodParams.ToArray()
            }
        };

        var date = DateTimeOffset.FromUnixTimeMilliseconds(timestamp);
        var now = DateTimeOffset.FromUnixTimeMilliseconds(context.View.Timestamp);

        // TODO: temporary fix to allow weekly schedule on same day
        if (date.Date == now.Date)
        {
            timestamp = date.AddDays(7)
                .ToUnixTimeMilliseconds();
        }

        var transaction = new Transaction
        {
            TransactionType = TransactionType.CONTRACT_SCHEDULED_SELF_CALL,
            PublicKey = context.Transaction.PublicKey,
            To = context.Contract.Address,
            Value = 0,
            MaxFee = (uint)maxFee,
            Timestamp = timestamp,
            Data = Serializer.Serialize(payload),
            ExecutionResult = ExecutionResult.SCHEDULED
        };

        // context.Logger.LogDebug("Schedule {method} at {time} for contract {address}", method, DateTimeOffset.FromUnixTimeMilliseconds(timestamp), context.Contract.Address);

        context.ScheduledCalls.Add(transaction);
        context.MethodParams.Clear();
    };

    private void WASIShims(Linker linker, Store store)
    {
        linker.Define("wasi_snapshot_preview1", "environ_get", Function.FromCallback<int, int, int>(store, (Caller caller, int environ, int environ_buf) =>
        {
            return 0;
        }));

        linker.Define("wasi_snapshot_preview1", "environ_sizes_get", Function.FromCallback<int, int, int>(store, (Caller caller, int environCount, int environSize) =>
        {
            return 0;
        }));

        linker.Define("wasi_snapshot_preview1", "clock_time_get", Function.FromCallback<int, long, int, int>(store, (Caller caller, int number, long precision, int time) =>
        {
            return 0;
        }));

        linker.Define("wasi_snapshot_preview1", "fd_close", Function.FromCallback<int, int>(store, (Caller caller, int fd) =>
        {
            return 0;
        }));

        linker.Define("wasi_snapshot_preview1", "fd_fdstat_get", Function.FromCallback<int, int, int>(store, (Caller caller, int fd, int fdStat) =>
        {
            return 0;
        }));

        linker.Define("wasi_snapshot_preview1", "fd_fdstat_set_flags", Function.FromCallback<int, int, int>(store, (Caller caller, int fd, int flags) =>
        {
            return 0;
        }));

        linker.Define("wasi_snapshot_preview1", "fd_prestat_get", Function.FromCallback<int, int, int>(store, (Caller caller, int fd, int bufPtr) =>
        {
            return 8; // __WASI_ERRNO_BADF
        }));

        linker.Define("wasi_snapshot_preview1", "fd_prestat_dir_name", Function.FromCallback<int, int, int, int>(store, (Caller caller, int fd, int pathPtr, int pathLen) =>
        {
            return 0;
        }));

        linker.Define("wasi_snapshot_preview1", "fd_read", Function.FromCallback<int, int, int, int, int>(store, (Caller caller, int fd, int iovsPtr, int iovsLen, int nreadPtr) =>
        {
            return 0;
        }));

        linker.Define("wasi_snapshot_preview1", "fd_seek", Function.FromCallback<int, long, int, int, int>(store, (Caller caller, int fd, long offset, int whence, int offsetOutPtr) =>
        {
            return 0;
        }));

        linker.Define("wasi_snapshot_preview1", "fd_write", Function.FromCallback<int, int, int, int, int>(store, (Caller caller, int fd, int iovsPtr, int iovsLen, int nwrittenPtr) =>
        {
            return 0;
        }));

        linker.Define("wasi_snapshot_preview1", "fd_advise", Function.FromCallback<int, long, long, int, int>(store, (Caller caller, int fd, long offset, long len, int advice) =>
        {
            return 0;
        }));

        linker.Define("wasi_snapshot_preview1", "fd_filestat_get", Function.FromCallback<int, int, int>(store, (Caller caller, int fd, int size) =>
        {
            return 0;
        }));

        linker.Define("wasi_snapshot_preview1", "fd_filestat_set_size", Function.FromCallback<int, long, int>(store, (Caller caller, int fd, long size) =>
        {
            return 0;
        }));

        linker.Define("wasi_snapshot_preview1", "fd_pread", Function.FromCallback<int, int, int, long, int, int>(store, (Caller caller, int fd, int size, int a, long b, int c) =>
        {
            return 0;
        }));

        linker.Define("wasi_snapshot_preview1", "fd_readdir", Function.FromCallback<int, int, int, long, int, int>(store, (Caller caller, int fd, int buf, int len, long cookie, int retptr0) =>
        {
            return 0;
        }));

        linker.Define("wasi_snapshot_preview1", "path_open", Function.FromCallback<int, int, int, int, int, long, long, int, int, int>(store, (Caller caller, int fd, int dirflags, int pathPtr, int pathLen, int ofFlags, long fsRightsBase, long fsRightsInheriting, int fdFlags, int openedFdPtr) =>
        {
            return 0;
        }));

        linker.Define("wasi_snapshot_preview1", "path_filestat_get", Function.FromCallback<int, int, int, int, int, int>(store, (Caller caller, int fd, int flags, int path, int retptr0, int a) =>
        {
            return 0;
        }));

        linker.Define("wasi_snapshot_preview1", "path_readlink", Function.FromCallback<int, int, int, int, int, int, int>(store, (Caller caller, int fd, int path, int buf, int buf_len, int retptr0, int a) =>
        {
            return 0;
        }));

        linker.Define("wasi_snapshot_preview1", "path_unlink_file", Function.FromCallback<int, int, int, int>(store, (Caller caller, int fd, int path, int a) =>
        {
            return 0;
        }));

        linker.Define("wasi_snapshot_preview1", "poll_oneoff", Function.FromCallback<int, int, int, int, int>(store, (Caller caller, int inPtr, int outPtr, int nsubscriptions, int u) =>
        {
            return 0;
        }));

        linker.Define("wasi_snapshot_preview1", "proc_exit", Function.FromCallback<int>(store, (Caller caller, int exitCode) =>
        {

        }));

        linker.Define("wasi_snapshot_preview1", "sched_yield", Function.FromCallback<int>(store, (Caller caller) =>
        {
            return 0;
        }));

        linker.Define("wasi_snapshot_preview1", "random_get", Function.FromCallback<int, int, int>(store, (Caller caller, int buf, int len) =>
        {
            return 0;
        }));
    }
}
