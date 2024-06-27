namespace Kryolite.Type;

public static class HashCodeHelper
{
    public static int CalculateHashCode(ReadOnlySpan<byte> buffer)
    {
        var hashCode = new HashCode();
        hashCode.AddBytes(buffer);
        return hashCode.ToHashCode();
    }
}
