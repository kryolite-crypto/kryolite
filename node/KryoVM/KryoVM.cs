using Kryolite.Shared;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using System;
using System.Text;
using System.Text.Json;
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
            .WithFuelConsumption(true)
            .WithReferenceTypes(true));

        var errors = Module.Validate(Engine, bytes);

        if (errors != null)
        {
            throw new Exception(errors);
        }

        Module = Module.FromBytes(Engine, "kryolite", bytes);
        Linker = new Linker(Engine);
        Store = new Store(Engine);

        // TODO: placeholder
        ulong start = 1000000000;
        Store.AddFuel(start);

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

    public int Initialize()
    {
        var init = Instance.GetFunction("__init");

        if (init == null)
        {
            throw new Exception("Contract initialization failed, init method missing");
        }

        return (int?)init.Invoke() ?? throw new Exception("Contract initialization failed, pointer to EntryPoint missing");
    }

    public int CallMethod(string method, object[] methodParams, out string? returns)
    {
        if (Context is null)
        {
            throw new Exception("Context not set");
        }

        var malloc = Instance.GetFunction("__malloc") ?? throw new Exception($"method not found [__malloc]");
        var free = Instance.GetFunction("__free") ?? throw new Exception($"method not found [__free]");
        var run = Instance.GetFunction(method) ?? throw new Exception($"method not found [{method}]");

        var memory = Instance.GetMemory("memory") ?? throw new Exception("memory not found");

        var ctr = Instance.GetGlobal("_CONTRACT") ?? throw new Exception("Context global not found");
        var ctrPtr = (int?)ctr.GetValue() ?? throw new Exception("Context global ptr not found");
        memory.WriteBuffer(ctrPtr, Context.Contract.Address);
        memory.WriteBuffer(ctrPtr + 26, Context.Contract.Owner);
        memory.WriteInt64(ctrPtr + 52, (long)Context.Contract.Balance);

        var tx = Instance.GetGlobal("_TRANSACTION") ?? throw new Exception("Transaction global not found");
        var txPtr = (int?)tx.GetValue() ?? throw new Exception("Transaction global ptr not found");
        memory.WriteBuffer(txPtr, Context.Transaction.From ?? new Address());
        memory.WriteBuffer(txPtr + 26, Context.Transaction.To!);
        memory.WriteInt64(txPtr + 52, (long)Context.Transaction.Value);

        var exitCode = 0;

        try
        {
            var values = new List<ValueBox>() { new IntPtr((int)methodParams[0]) };
            var toFree = new List<(int ptr, int length)>();

            var manifest = Context.Contract.Manifest.Methods.Where(x => x.Name == method).First();

            if (manifest == null)
            {
                throw new Exception($"method manifest not found ({method})");
            }

            var mParams = manifest.Params.ToArray();

            for (int i = 0; i < methodParams.Length - 1; i++)
            {
                var param = mParams[i];
                var value = ValueConverter.ConvertFromValue(param.Type, methodParams[i + 1]);

                switch (value)
                {
                    case byte[] arr:
                        var ptr = (int?)malloc.Invoke(arr.Length);

                        if (ptr == null)
                        {
                            throw new Exception($"failed to allocate memory");
                        }

                        memory.WriteBuffer((int)ptr, arr);
                        values.Add((int)ptr);
                        toFree.Add(((int)ptr, arr.Length));
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

            run.Invoke(values.ToArray());

            foreach (var ptr in toFree)
            {
                free.Invoke(ptr.ptr, ptr.length);
            }
        }
        catch (WasmtimeException waEx)
        {
            Context.Logger.LogError(waEx, "contract execution failed");

            if (waEx.InnerException is ExitException eEx)
            {
                exitCode = eEx.ExitCode;
            }
            else
            {
                exitCode = 20;
            }
        }
        catch (Exception ex)
        {
            Context.Logger.LogError(ex, "contract execution failed");
            exitCode = 1;
        }

        returns = Context!.Returns;
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
    private void RegisterAPI() 
    {
        Linker.Define("env", "__transfer", Function.FromCallback<int, long>(Store, (Caller caller, int address, long value) => {
            var memory = caller.GetMemory("memory");

            if (memory is null) 
            {
                return;
            }

            var addr = memory.ReadAddress(address);

            if (addr is null) 
            {
                //throw new Exception("unable to transfer, null 'to' address");
                throw new ExitException(101);

            }

            var balance = checked(Context!.Balance - value);

            if (balance < 0)
            {
                //throw new Exception("unable to transfer, contract balance negative");
                throw new ExitException(102);
            }

            Context!.Balance = balance;
            Context!.Transaction.Effects.Add(new Effect(Context!.Contract.Address, Context.Contract.Address, addr, value));
        }));

        Linker.Define("env", "__approval", Function.FromCallback<int, int, int>(Store, (Caller caller, int fromPtr, int toPtr, int tokenIdPtr) => {
            var memory = caller.GetMemory("memory");

            if (memory is null)
            {
                return;
            }

            var eventData = new ApprovalEventArgs
            {
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
                Owner = memory.ReadAddress(ownerPtr) ?? throw new Exception("__transfer_token: null 'tokenIdPtr' address"),
                TokenId = memory.ReadU256(tokenIdPtr) ?? throw new Exception("__transfer_token: null 'tokenIdPtr' address")
            };

            Context!.Events.Add(eventData);
            Context!.Transaction.Effects.Add(new Effect(Context!.Contract.Address, Context.Contract.Address, eventData.Owner, 0, eventData.TokenId, true));
        }));

        Linker.Define("env", "__println", Function.FromCallback(Store, (Caller caller, int type_ptr, int type_len, int ptr, int len) => {
            var mem = caller.GetMemory("memory");

            if (mem is null)
            {
                return;
            }

            var type = mem.GetSpan(type_ptr, type_len);
            var msg = mem.GetSpan(ptr, len);
            Console.WriteLine("LOG: " + ValueConverter.ConvertToValue(type, msg));
        }));

        Linker.Define("env", "__append_event", Function.FromCallback(Store, (Caller caller, int type_ptr, int type_len, int ptr, int len) => {
            var mem = caller.GetMemory("memory");

            if (mem is null)
            {
                return;
            }

            var type = mem.GetSpan(type_ptr, type_len);
            var msg = mem.GetSpan(ptr, len);
            Context!.EventData.Add(ValueConverter.ConvertToValue(type, msg));
        }));

        Linker.Define("env", "__publish_event", Function.FromCallback(Store, (Caller caller) => {
            var memory = caller.GetMemory("memory");

            if (memory is null)
            {
                return;
            }

            var eventData = new GenericEventArgs
            {
                Json = JsonSerializer.Serialize(Context!.EventData)
            };

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
            Console.WriteLine("Returns: " + str);
        }));

        Linker.Define("env", "__rand", Function.FromCallback<float>(Store, (Caller caller) => {
            return Context!.Rand.NextSingle();
        }));

        Linker.Define("env", "__exit", Function.FromCallback<int>(Store, (Caller caller, int exitCode) => {
            throw new ExitException(exitCode);
        }));
    }
}
