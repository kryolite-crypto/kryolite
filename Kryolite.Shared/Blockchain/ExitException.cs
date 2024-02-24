namespace Kryolite.Shared;

public class ExitException : Exception
{
    public int ExitCode;

    public ExitException(int exitCode)
    {
        ExitCode = exitCode;
    }
}
