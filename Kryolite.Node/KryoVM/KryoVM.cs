using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;
using Wasmtime;

namespace Kryolite.Node;

public class KryoVM : IDisposable
{
    private VMContext? Context { get; set; }
    private Engine Engine { get; set; }
    private Module Module { get; set; }
    private Linker Linker { get; set; }
    private Store Store { get; set; }
    private Instance Instance { get; set; }

    private KryoVM(ReadOnlySpan<byte> bytes)
    {
        Engine = new Engine(new Config()
            .WithFuelConsumption(true));

        var errors = Module.Validate(Engine, bytes);

        if (errors != null)
        {
            throw new Exception(errors);
        }

        Module = Module.FromBytes(Engine, "kryolite", bytes);
        Linker = new Linker(Engine);
        Store = new Store(Engine);

        if (Context?.Logger.IsEnabled(LogLevel.Debug) ?? false)
        {
            foreach (var imp in Module.Imports)
            {
                Console.WriteLine($"{imp.ModuleName} - {imp.Name}");
            }

            foreach (var exp in Module.Exports)
            {
                Console.WriteLine($"{exp.Name}");
            }
        }

        // TODO: placeholder
        Store.Fuel = 1000000000;

        RegisterAPI();

        Instance = Linker.Instantiate(Store, Module);

        _ = Instance.GetFunction("__malloc") ?? throw new Exception($"method not found [__malloc]");
        _ = Instance.GetFunction("__free") ?? throw new Exception($"method not found [__free]");
    }

    public static KryoVM LoadFromCode(ReadOnlySpan<byte> code)
    {
        return new KryoVM(code);
    }

    public static KryoVM LoadFromSnapshot(ReadOnlySpan<byte> code, ReadOnlySpan<byte> snapshot)
    {
        var vm = new KryoVM(code);

        var memory = vm.Instance.GetMemory("memory") ?? throw new Exception("vm memory initialization failed");
        var data = snapshot.Decompress();
        var size = data.Length;

        while (memory.GetLength() < size)
        {
            memory.Grow(1);
        }

        unsafe
        {
            var buf = new Span<byte>(memory.GetPointer().ToPointer(), size);
            data.CopyTo(buf);
        }

        return vm;
    }

    public KryoVM WithContext(VMContext context)
    {
        Context = context ?? throw new ArgumentNullException(nameof(context));

        return this;
    }

    public void Initialize()
    {
        var init = Instance.GetFunction("_initialize") ?? throw new Exception($"method not found [_initialize]");
        init.Invoke();

        SetContext();

        var install = Instance.GetFunction("__install") ?? throw new Exception($"method not found [__install]");
        install.Invoke();
    }

    public int CallMethod(string method, object[] methodParams, out string? returns)
    {
        if (Context is null)
        {
            throw new Exception("Context not set");
        }

        var toFree = new List<(int ptr, int length)>();

        var memory = Instance.GetMemory("memory") ?? throw new Exception("memory not found");
        var run = Instance.GetFunction(method) ?? throw new Exception($"method not found [{method}]");
        var malloc = Instance.GetFunction("__malloc") ?? throw new Exception($"method not found [__malloc]");
        var free = Instance.GetFunction("__free") ?? throw new Exception($"method not found [__free]");

        var exitCode = 0;

        try
        {
            SetContext();

            var manifest = Context.Contract.Manifest?.Methods.Where(x => x.Name == method).First() ?? throw new Exception("contract manifest not found");
            var mParams = manifest.Params.ToArray();
            var values = new List<ValueBox>();

            for (int i = 0; i < methodParams.Length; i++)
            {
                var param = mParams[i];
                var value = ValueConverter.ConvertFromValue(param.Type, methodParams[i]);

                switch (value)
                {
                    case byte[] arr:
                        var ptr = (int)(malloc.Invoke(arr.Length) ?? throw new Exception($"failed to allocate memory"));
    
                        // Write buffer to program
                        memory.WriteBuffer(ptr, arr);

                        values.Add(ptr);
                        values.Add(arr.Length);
                        toFree.Add((ptr, arr.Length));
                        break;
                    case bool val:
                        values.Add(val == true ? 1 : 0);
                        break;
                    case byte val:
                        values.Add(val);
                        break;
                    case short val:
                        values.Add(val);
                        break;
                    case ushort val:
                        values.Add(val);
                        break;
                    case int val:
                        values.Add(val);
                        break;
                    case uint val:
                        values.Add(val);
                        break;
                    case long val:
                        values.Add(val);
                        break;
                    case ulong val:
                        values.Add(val);
                        break;
                    case float val:
                        values.Add(val);
                        break;
                    case double val:
                        values.Add(val);
                        break;
                }
            }

            if (values.Count > 0)
            {
                run.Invoke(values.ToArray());
            }
            else
            {
                run.Invoke();
            }

            foreach (var ptr in toFree)
            {
                free.Invoke(ptr.ptr, ptr.length);
            }
        }
        catch (WasmtimeException waEx)
        {
            if (waEx.InnerException is ExitException eEx)
            {
                exitCode = eEx.ExitCode;

                if (exitCode == 127)
                {
                    Context.Logger.LogDebug("Contract execution exited with assert failure");
                }
                else
                {
                    Context.Logger.LogError(waEx, "contract execution failed with unknown exit code ({err})", exitCode);
                }
            }
            else
            {
                Context.Logger.LogError(waEx, "contract execution failed");
                exitCode = 20;
            }
        }
        catch (Exception ex)
        {
            Context.Logger.LogError(ex, "contract execution failed");
            exitCode = 1;
        }

        returns = Context?.Returns?.ToString();
        return exitCode;
    }

    public byte[] TakeSnapshot()
    {
        var memory = Instance.GetMemory("memory") ?? throw new Exception("memory not found");
        return memory.GetSpan(0, (int)memory.GetLength()).Compress();
    }

    public void Dispose()
    {
        Engine.Dispose();
        Module.Dispose();
        Linker.Dispose();
        Store.Dispose();
    }

    private void SetContext()
    {
        var memory = Instance.GetMemory("memory") ?? throw new Exception("memory not found");
        var malloc = Instance.GetFunction("__malloc") ?? throw new Exception($"method not found [__malloc]");
        var free = Instance.GetFunction("__free") ?? throw new Exception($"method not found [__free]");
        var setContract = Instance.GetFunction("set_contract") ?? throw new Exception($"method not found [set_contract]");
        var setTransaction = Instance.GetFunction("set_transaction") ?? throw new Exception($"method not found [set_transaction]");
        var setView = Instance.GetFunction("set_view") ?? throw new Exception($"method not found [set_view]");

        // Set Contract details to smart contract
        var addrPtr = (int)(malloc.Invoke(Address.ADDRESS_SZ) ?? throw new Exception($"failed to allocate memory"));
        memory.WriteBuffer(addrPtr, (byte[])Context!.Contract.Address);

        var ownerPtr = (int)(malloc.Invoke(Address.ADDRESS_SZ) ?? throw new Exception($"failed to allocate memory"));
        memory.WriteBuffer(ownerPtr, (byte[])Context!.Contract.Owner);

        setContract.Invoke(addrPtr, Address.ADDRESS_SZ, ownerPtr, Address.ADDRESS_SZ, (long)Context!.Balance);
        free.Invoke(addrPtr, Address.ADDRESS_SZ);
        free.Invoke(ownerPtr, Address.ADDRESS_SZ);

        // Set transaction details to smart contract
        var fromPtr = (int)(malloc.Invoke(Address.ADDRESS_SZ) ?? throw new Exception($"failed to allocate memory"));
        memory.WriteBuffer(fromPtr, (byte[])Context.Transaction.From!);

        setTransaction.Invoke(fromPtr, Address.ADDRESS_SZ, (long)Context.Transaction.Value);
        free.Invoke(fromPtr, Address.ADDRESS_SZ);

        // Set view details to smart contract
        setView.Invoke(Context.View.Id, Context.View.Timestamp);
    }

    private void RegisterAPI() 
    {
        Linker.Define("env", "__transfer", Function.FromCallback<int, long>(Store, (Caller caller, int address, long value) => {
            var memory = caller.GetMemory("memory");

            if (memory is null) 
            {
                return;
            }

            if (value < 0)
            {
                throw new Exception("__transfer: negative value");
            }

            var addr = memory.ReadAddress(address);

            if (addr is null) 
            {
                throw new ExitException(101);
            }

            var balance = checked(Context!.Balance - (ulong)value);

            if (balance < 0)
            {
                throw new ExitException(102);
            }

            Context!.Balance = balance;
            Context!.Transaction.Effects.Add(new Effect(Context!.Contract.Address, Context.Contract.Address, addr, (ulong)value));
        }));

        Linker.Define("env", "__approval", Function.FromCallback<int, int, int>(Store, (Caller caller, int fromPtr, int toPtr, int tokenIdPtr) => {
            var memory = caller.GetMemory("memory");

            if (memory is null)
            {
                return;
            }

            var eventData = new ApprovalEventArgs
            {
                Contract = Context!.Contract.Address,
                From = memory.ReadAddress(fromPtr) ?? throw new Exception("__approval: null 'from' address"),
                To = memory.ReadAddress(toPtr) ?? throw new Exception("__approval: null 'to' address"),
                TokenId = memory.ReadU256(tokenIdPtr) ?? throw new Exception("__approval: null 'tokenIdPtr' address")
            };

            Context!.Events.Add(eventData);
        }));

        Linker.Define("env", "__transfer_token", Function.FromCallback<int, int, int>(Store, (Caller caller, int fromPtr, int toPtr, int tokenIdPtr) => {
            var memory = caller.GetMemory("memory");

            if (memory is null)
            {
                return;
            }

            var tokenId = memory.ReadU256(tokenIdPtr) ?? throw new Exception("__transfer_token: null 'tokenIdPtr' address");
            var from = memory.ReadAddress(fromPtr) ?? throw new Exception("__transfer_token: null 'from' address");
            var to = memory.ReadAddress(toPtr) ?? throw new Exception("__transfer_token: null 'to' address");

            var eventData = new TransferTokenEventArgs
            {
                Contract = Context!.Contract.Address,
                From = from,
                To = to,
                TokenId = tokenId
            };

            Context!.Events.Add(eventData);
            Context!.Transaction.Effects.Add(new Effect(Context!.Contract.Address, from, to, 0, tokenId));
        }));

        Linker.Define("env", "__consume_token", Function.FromCallback<int, int>(Store, (Caller caller, int ownerPtr, int tokenIdPtr) => {
            var memory = caller.GetMemory("memory");

            if (memory is null)
            {
                return;
            }

            var eventData = new ConsumeTokenEventArgs
            {
                Contract = Context!.Contract.Address,
                Owner = memory.ReadAddress(ownerPtr) ?? throw new Exception("__transfer_token: null 'tokenIdPtr' address"),
                TokenId = memory.ReadU256(tokenIdPtr) ?? throw new Exception("__transfer_token: null 'tokenIdPtr' address")
            };

            Context!.Events.Add(eventData);
            Context!.Transaction.Effects.Add(new Effect(Context!.Contract.Address, Context.Contract.Address, eventData.Owner, 0, eventData.TokenId, true));
        }));

        Linker.Define("env", "__println", Function.FromCallback(Store, (Caller caller, int ptr, int len) => {
            var mem = caller.GetMemory("memory");

            if (mem is null)
            {
                return;
            }

            var msg = mem.GetSpan(ptr, len);
            Context?.Logger.LogDebug($"LOG: {Encoding.UTF8.GetString(msg)}");
        }));

        Linker.Define("env", "__append_event", Function.FromCallback(Store, (Caller caller, int ptr, int len) => {
            var mem = caller.GetMemory("memory");

            if (mem is null)
            {
                return;
            }

            var msg = mem.GetSpan(ptr, len);
            Context!.EventData.Add(Encoding.UTF8.GetString(msg));
        }));

        Linker.Define("env", "__publish_event", Function.FromCallback(Store, (Caller caller) => {
            var memory = caller.GetMemory("memory");

            if (memory is null)
            {
                return;
            }

            var eventData = new GenericEventArgs
            {
                Contract = Context!.Contract.Address
            };

            eventData.EventData.AddRange(Context!.EventData);

            Context!.Events.Add(eventData);
            Context!.EventData.Clear();
        }));

        Linker.Define("env", "__return", Function.FromCallback(Store, (Caller caller, int ptr, int len) => {
            var mem = caller.GetMemory("memory");

            if (mem is null)
            {
                return;
            }

            var str = mem.ReadString(ptr, len, Encoding.UTF8);
            Context!.Returns = str;
        }));

        Linker.Define("env", "__rand", Function.FromCallback<float>(Store, (Caller caller) => {
            return Context!.Rand.NextSingle();
        }));

        Linker.Define("env", "__exit", Function.FromCallback<int>(Store, (Caller caller, int exitCode) => {
            throw new ExitException(exitCode);
        }));

        Linker.Define("env", "__hash_data", Function.FromCallback(Store, (Caller caller, int dataPtr, int dataLen, int destPtr, int destLen) => {
            var mem = caller.GetMemory("memory") ?? throw new Exception("memory not found");
            
            var data = mem.GetSpan<byte>(dataPtr, dataLen);
            var dest = mem.GetSpan<byte>(destPtr, destLen);

            if (!SHA256.TryHashData(data, dest, out _))
            {
                throw new Exception("hash failed");
            }
        }));

        Linker.Define("env", "__schedule_param", Function.FromCallback(Store, (Caller caller, int paramPtr, int paramLen) => {
            var mem = caller.GetMemory("memory");

            if (mem is null)
            {
                return;
            }

            var param = mem.ReadString(paramPtr, paramLen, Encoding.UTF8);
            Context!.MethodParams.Add(param);
        }));

        Linker.Define("env", "__schedule", Function.FromCallback(Store, (Caller caller, int methodPtr, int methodLen, long timestamp) => {
            var mem = caller.GetMemory("memory");

            if (mem is null)
            {
                return;
            }

            var method = mem.ReadString(methodPtr, methodLen, Encoding.UTF8);

            var payload = new TransactionPayload
            {
                Payload = new CallMethod
                {
                    Method = method,
                    Params = [.. Context!.MethodParams]
                }
            };

            var transaction = new Transaction {
                TransactionType = TransactionType.CONTRACT_SCHEDULED_SELF_CALL,
                PublicKey = Context!.Transaction.PublicKey,
                To = Context!.Contract.Address,
                Value = 0,
                Timestamp = timestamp,
                Data = Serializer.Serialize<TransactionPayload>(payload),
                ExecutionResult = ExecutionResult.SCHEDULED
            };

            Console.WriteLine($"schedule {method} at {DateTimeOffset.FromUnixTimeMilliseconds(timestamp)}");

            Context.ScheduledCalls.Add(transaction);
            Context.MethodParams.Clear();
        }));

        // WASI shims
        Linker.Define("wasi_snapshot_preview1", "environ_get", Function.FromCallback<int, int, int>(Store, (Caller caller, int environ, int environ_buf) => {
            return 0;
        }));

        Linker.Define("wasi_snapshot_preview1", "environ_sizes_get", Function.FromCallback<int, int, int>(Store, (Caller caller, int environCount, int environSize) => {
            return 0;
        }));

        Linker.Define("wasi_snapshot_preview1", "clock_time_get", Function.FromCallback<int, long, int, int>(Store, (Caller caller, int number, long precision, int time) => {
            return 0;
        }));

        Linker.Define("wasi_snapshot_preview1", "fd_close", Function.FromCallback<int, int>(Store, (Caller caller, int fd) => {
            return 0;
        }));

        Linker.Define("wasi_snapshot_preview1", "fd_fdstat_get", Function.FromCallback<int, int, int>(Store, (Caller caller, int fd, int fdStat) => {
            return 0;
        }));

        Linker.Define("wasi_snapshot_preview1", "fd_fdstat_set_flags", Function.FromCallback<int, int, int>(Store, (Caller caller, int fd, int flags) => {
            return 0;
        }));

        Linker.Define("wasi_snapshot_preview1", "fd_prestat_get", Function.FromCallback<int, int, int>(Store, (Caller caller, int fd, int bufPtr) => {
            return 8; // __WASI_ERRNO_BADF
        }));

        Linker.Define("wasi_snapshot_preview1", "fd_prestat_dir_name", Function.FromCallback<int, int, int, int>(Store, (Caller caller, int fd, int pathPtr, int pathLen) => {
            return 0;
        }));

        Linker.Define("wasi_snapshot_preview1", "fd_read", Function.FromCallback<int, int, int, int, int>(Store, (Caller caller, int fd, int iovsPtr, int iovsLen, int nreadPtr) => {
            return 0;
        }));

        Linker.Define("wasi_snapshot_preview1", "fd_seek", Function.FromCallback<int, long, int, int, int>(Store, (Caller caller, int fd, long offset, int whence, int offsetOutPtr) => {
            return 0;
        }));

        Linker.Define("wasi_snapshot_preview1", "fd_write", Function.FromCallback<int, int, int, int, int>(Store, (Caller caller, int fd, int iovsPtr, int iovsLen, int nwrittenPtr) => {
            return 0;
        }));

        Linker.Define("wasi_snapshot_preview1", "fd_advise", Function.FromCallback<int, long, long, int, int>(Store, (Caller caller, int fd, long offset, long len, int advice) => {
            return 0;
        }));

        Linker.Define("wasi_snapshot_preview1", "fd_filestat_get", Function.FromCallback<int, int, int>(Store, (Caller caller, int fd, int size) => {
            return 0;
        }));

        Linker.Define("wasi_snapshot_preview1", "fd_filestat_set_size", Function.FromCallback<int, long, int>(Store, (Caller caller, int fd, long size) => {
            return 0;
        }));

        Linker.Define("wasi_snapshot_preview1", "fd_pread", Function.FromCallback<int, int, int, long, int, int>(Store, (Caller caller, int fd, int size, int a, long b, int c) => {
            return 0;
        }));

        Linker.Define("wasi_snapshot_preview1", "fd_readdir", Function.FromCallback<int, int, int, long, int, int>(Store, (Caller caller, int fd, int buf, int len, long cookie, int retptr0) => {
            return 0;
        }));

        Linker.Define("wasi_snapshot_preview1", "path_open", Function.FromCallback<int, int, int, int, int, long, long, int, int, int>(Store, (Caller caller, int fd, int dirflags, int pathPtr, int pathLen, int ofFlags, long fsRightsBase, long fsRightsInheriting, int fdFlags, int openedFdPtr) => {
            return 0;
        }));

        Linker.Define("wasi_snapshot_preview1", "path_filestat_get", Function.FromCallback<int, int, int, int, int, int>(Store, (Caller caller, int fd, int flags, int path, int retptr0, int a) => {
            return 0;
        }));

        Linker.Define("wasi_snapshot_preview1", "path_readlink", Function.FromCallback<int, int, int, int, int, int, int>(Store, (Caller caller, int fd, int path, int buf, int buf_len, int retptr0, int a) => {
            return 0;
        }));

        Linker.Define("wasi_snapshot_preview1", "path_unlink_file", Function.FromCallback<int, int, int, int>(Store, (Caller caller, int fd, int path, int a) => {
            return 0;
        }));

        Linker.Define("wasi_snapshot_preview1", "poll_oneoff", Function.FromCallback<int, int, int, int, int>(Store, (Caller caller, int inPtr, int outPtr, int nsubscriptions, int u) => {
            return 0;
        }));

        Linker.Define("wasi_snapshot_preview1", "proc_exit", Function.FromCallback<int>(Store, (Caller caller, int exitCode) => {

        }));

        Linker.Define("wasi_snapshot_preview1", "sched_yield", Function.FromCallback<int>(Store, (Caller caller) => {
            return 0;
        }));

        Linker.Define("wasi_snapshot_preview1", "random_get", Function.FromCallback<int, int, int>(Store, (Caller caller, int buf, int len) => {
            return 0;
        }));
    }
}
