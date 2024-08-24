namespace Kryolite.Module.SmartContract;

public class VirtualMachine : IDisposable
{
    public long Size => 0;

    public VirtualMachine(ReadOnlySpan<byte> code)
    {

    }

    public void Dispose()
    {
        throw new NotImplementedException();
    }
}
