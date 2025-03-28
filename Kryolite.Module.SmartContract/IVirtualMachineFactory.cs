namespace Kryolite.Module.SmartContract;

public interface IVirtualMachineFactory
{
    VirtualMachine Create(IContext context, ReadOnlySpan<byte> code);
    VirtualMachine Load(IContext context);
}
