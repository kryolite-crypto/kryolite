namespace Kryolite;

public class StartupSequence
{
    public ManualResetEvent Blockchain { get; } = new ManualResetEvent(false);
    public ManualResetEvent Mempool { get; } = new ManualResetEvent(false);
    public ManualResetEvent Network { get; } = new ManualResetEvent(false);
}
