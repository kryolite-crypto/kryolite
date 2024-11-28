namespace Kryolite.Module.SmartContract;

public class InvalidModuleException : Exception
{
    public InvalidModuleException(string message) : base(message)
    {

    }

    public static void ThrowIfNotNull(string? value)
    {
        if (value is not null)
        {
            throw new InvalidModuleException(value);
        }
    }
}
