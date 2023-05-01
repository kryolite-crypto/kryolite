namespace Kryolite;

public class StartupSequence
{
    public ManualResetEvent Blockchain { get; } = new ManualResetEvent(false);
    public ManualResetEvent Network { get; } = new ManualResetEvent(false);
    public ManualResetEvent Application { get; } = new ManualResetEvent(false);
}
