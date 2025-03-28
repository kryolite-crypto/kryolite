using System.IO.Compression;
using System.Text;
using Kryolite.Module.SmartContract.ABI;
using Kryolite.Type;
using Kryolite.Model;
using Microsoft.Extensions.Logging;
using Wasmtime;
using Mod = Wasmtime.Module;

namespace Kryolite.Module.SmartContract;

public class VirtualMachine : IDisposable
{
    public long Size => 0;
    public IContext Context => _context;
    public Memory Memory => _instance.GetMemory("memory") ?? throw new Exception("VirtualMachine memory initialization failed");

    private IContext _context;
    private Engine _engine;
    private Mod _module;
    private Linker _linker;
    private Store _store;
    private Instance _instance;

    private ILogger _logger;

    // Cached wasm functions and memory
    private Memory _memory;
    private Func<int, int> _malloc;
    private Action<int, int> _free;

    // ABI stuff
    private ICallingConv _callingConv;
    private IStandardApi _api;

    private static Config _config = new Config()
        .WithCraneliftNaNCanonicalization(true)
        .WithFuelConsumption(true);

    public VirtualMachine(ReadOnlySpan<byte> code, IContext context, ILogger logger)
    {
        _engine = new(_config);

        InvalidModuleException.ThrowIfNotNull(Mod.Validate(_engine, code));

        _module = Mod.FromBytes(_engine, "kryolite", code);
        _linker = new(_engine);
        _store = new(_engine);
        _instance = _linker.Instantiate(_store, _module);

        _context = context;
        _logger = logger;

        _memory = _instance.GetMemory("memory") ?? throw new Exception("memory not found");
        _malloc = _instance.GetFunction<int, int>("__malloc") ?? throw new Exception($"method not found [__malloc]");
        _free = _instance.GetAction<int, int>("__free") ?? throw new Exception($"method not found [__free]");

        _callingConv = ICallingConv.Select(context.Contract);
        _api = IStandardApi.Select(context.Contract);
    }

    public void Initialize()
    {
        var init = _instance.GetFunction("_initialize") ?? throw new Exception($"method not found [_initialize]");
        init.Invoke();

        SetContext();

        var install = _instance.GetFunction("__install") ?? throw new Exception($"method not found [__install]");
        install.Invoke();
    }

    public void AddFuel(ulong fuel)
    {
        _store.AddFuel(fuel);
    }

    public ulong GetConsumedFuel()
    {
        return _store.GetConsumedFuel();
    }

    public VirtualMachine WithContext(IContext context)
    {
        _context = context;
        return this;
    }

    public byte[] TakeSnapshot()
    {
        var memory = Memory;
        var data = Memory.GetSpan(0, (int)memory.GetLength());

        using var output = new MemoryStream();
        using (var dstream = new DeflateStream(output, CompressionLevel.Optimal))
        {
            dstream.Write(data);
        }

        return output.ToArray();
    }

    public unsafe void RestoreSnapshot(ReadOnlySpan<byte> snapshot)
    {
        fixed (byte* sp = &snapshot.GetPinnableReference())
        {
            var memory = Memory;

            using var input = new UnmanagedMemoryStream(sp, snapshot.Length);
            using var dstream = new DeflateStream(input, CompressionMode.Decompress);

            byte[] buffer = new byte[8192];
            int bytesRead, position = 0;

            while ((bytesRead = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (memory.GetLength() < (position + bytesRead))
                {
                    memory.Grow(1);
                }

                var span = memory.GetSpan(position, bytesRead);
                buffer.CopyTo(span);
            }
        }
    }

    private void SetContext()
    {
        const int CTX_LEN = 406;
        var addr = _malloc(CTX_LEN);

        try
        {
            var span = _memory.GetSpan(addr, CTX_LEN);
            var pos = 0;

            _context.Contract.Address.Buffer.CopyTo(span.SliceAndIncrement(ref pos, Address.ADDRESS_SZ));
            _context.Contract.Owner.Buffer.CopyTo(span.SliceAndIncrement(ref pos, Address.ADDRESS_SZ));
            _context.Balance.CopyTo(span.SliceAndIncrement(ref pos, sizeof(long)));

            _context.Transaction.CalculateHash().Buffer.CopyTo(span.SliceAndIncrement(ref pos, SHA256Hash.HASH_SZ));
            _context.Transaction.TransactionType.CopyTo(span.SliceAndIncrement(ref pos, 1));
            _context.Transaction.PublicKey.Buffer.CopyTo(span.SliceAndIncrement(ref pos, PublicKey.PUB_KEY_SZ));
            _context.Transaction.To.Buffer.CopyTo(span.SliceAndIncrement(ref pos, Address.ADDRESS_SZ));
            _context.Transaction.Value.CopyTo(span.SliceAndIncrement(ref pos, sizeof(ulong)));
            _context.Transaction.MaxFee.CopyTo(span.SliceAndIncrement(ref pos, sizeof(uint)));
            _context.Transaction.Timestamp.CopyTo(span.SliceAndIncrement(ref pos, sizeof(long)));
            _context.Transaction.Signature.Buffer.CopyTo(span.SliceAndIncrement(ref pos, Signature.SIGNATURE_SZ));

            _context.View.Id.CopyTo(span.SliceAndIncrement(ref pos, sizeof(long)));
            _context.View.Timestamp.CopyTo(span.SliceAndIncrement(ref pos, sizeof(long)));
            _context.View.LastHash.Buffer.CopyTo(span.SliceAndIncrement(ref pos, SHA256Hash.HASH_SZ));
            _context.View.PublicKey.Buffer.CopyTo(span.SliceAndIncrement(ref pos, PublicKey.PUB_KEY_SZ));
            _context.View.Signature.Buffer.CopyTo(span.SliceAndIncrement(ref pos, Signature.SIGNATURE_SZ));

            var setContext = _instance.GetAction<int>("set_context") ?? throw new Exception($"method not found [set_context]");
            setContext((int)addr);
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to set SmartContract context: {message}", ex.Message);
            _logger.LogDebug(ex, "Exception");
        }
        finally
        {
            _free(addr, CTX_LEN);
        }
    }

    public int CallMethod(string method, string[] parameters, out string? returns)
    {
        var run = _instance.GetFunction(method) ?? throw new Exception($"method not found [{method}]");

        if (parameters.Length != run.Parameters.Count)
        {
            returns = null;
            return 47;
        }


        var exitCode = 0;
        var toFree = new List<(int, int)>();

        try
        {
            var manifest = Context.Contract.Manifest?.Methods.Where(x => x.Name == method).First() ?? throw new Exception("method manifest not found");
            var values = AssignValues(parameters, manifest, toFree);
            run.Invoke(values);
        }
        catch (WasmtimeException waEx) when (waEx.InnerException is ExitException eEx)
        {
            exitCode = eEx.ExitCode;

            if (exitCode == 127)
            {
                _logger.LogDebug("Contract execution exited with assert failure");
            }
            else
            {
                _logger.LogError(waEx, "Contract execution failed with unknown exit code ({err})", exitCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("Contract execution failed: {message}", ex.Message);
            _logger.LogDebug(ex, "Detailed exception");
            exitCode = 20;
        }
        finally
        {
            foreach (var (ptr, len) in toFree)
            {
                _free(ptr, len);
            }
        }

        returns = Context.ReturnValue.ToString();
        return exitCode;
    }

    public void Dispose()
    {
        _store.Dispose();
        _linker.Dispose();
        _module.Dispose();
        _engine.Dispose();
    }

    private ValueBox[] AssignValues(string[] parameters, ContractMethod method, List<(int, int)> toFree)
    {
        var values = new ValueBox[parameters.Length];

        for (var i = 0; i < parameters.Length; i++)
        {
            var param = method.Params[i];
            var val = param.Type switch
            {
                "bool" => (ValueBox)(bool.Parse(parameters[i]) == true ? 1 : 0),
                "byte" => (ValueBox)byte.Parse(parameters[i]),
                "short" => (ValueBox)short.Parse(parameters[i]),
                "int" => (ValueBox)int.Parse(parameters[i]),
                "long" => (ValueBox)long.Parse(parameters[i]),
                "float" => (ValueBox)float.Parse(parameters[i]),
                "double" => (ValueBox)double.Parse(parameters[i]),
                "Address" => CopyBuffer(((Address)parameters[i]).Buffer, toFree),
                "U256" => CopyBuffer(((SHA256Hash)parameters[i]).Buffer, toFree),
                _ => CopyBuffer(Encoding.UTF8.GetBytes(parameters[i]), toFree)
            };

            values[i] = val;
        }

        return values;
    }

    private ValueBox CopyBuffer(byte[] value, List<(int, int)> toFree)
    {
        var ptr = _malloc.Invoke(value.Length);
        var span = _memory.GetSpan(ptr, value.Length);

        value.CopyTo(span);

        toFree.Add((ptr, value.Length));

        return ptr;
    }
}
