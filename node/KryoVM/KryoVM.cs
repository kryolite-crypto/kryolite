using Kryolite;
using Kryolite.Shared;
using Makaretu.Dns.Resolving;
using MessagePack;
using System;
using System.Text;
using System.Text.Json;
using Wasmtime;

namespace node;

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

    public object CallMethod(string method, object[] methodParams)
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
        memory.WriteBuffer(txPtr + 26, Context.Transaction.To);
        memory.WriteInt64(txPtr + 52, (long)Context.Transaction.Value);

        return null;
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
        var eventData = new List<object>();

        Linker.Define("env", "__transfer", Function.FromCallback<int, long>(Store, (Caller caller, int address, long value) => {
            var memory = caller.GetMemory("memory");

            if (memory is null) 
            {
                return;
            }

            var addr = memory.ReadAddress(address);

            Context!.Transaction.Effects.Add(new Effect(addr, (ulong)value));
        }));

        Linker.Define("env", "__export_state", Function.FromCallback<int, int>(Store, (Caller caller, int ptr, int sz) => {
            var memory = caller.GetMemory("memory");

            if (memory is null)
            {
                return;
            }

            var keyLen = memory.ReadInt32(ptr);
            var keyStr = memory.GetSpan(ptr, sz);

            Console.WriteLine("STATE_EXPORT: " + MessagePackSerializer.ConvertToJson(keyStr.ToArray()));
            Store.SetData(MessagePackSerializer.ConvertToJson(keyStr.ToArray()));
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
            eventData.Add(ValueConverter.ConvertToValue(type, msg));
        }));

        Linker.Define("env", "__publish_event", Function.FromCallback(Store, (Caller caller) => {
            Console.WriteLine("EVENT: " + JsonSerializer.Serialize(eventData));
            eventData.Clear();
        }));

        Linker.Define("env", "__return", Function.FromCallback(Store, (Caller caller, int ptr, int len) => {
            var mem = caller.GetMemory("memory");

            if (mem is null)
            {
                return;
            }

            Console.WriteLine("Returns: " + mem.ReadString(ptr, len, Encoding.UTF8));
        }));

        Linker.Define("env", "__rand", Function.FromCallback<float>(Store, (Caller caller) => {
            return Context!.Rand.NextSingle();
        }));

        Linker.Define("env", "__exit", Function.FromCallback<int>(Store, (Caller caller, int exitCode) => {
            throw new ExitException(exitCode);
        }));
    }
}
