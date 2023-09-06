namespace Kryolite;

public class StartupSequence
{
    public ManualResetEventSlim Application { get; } = new(false);
}
