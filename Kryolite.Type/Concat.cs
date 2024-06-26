namespace Kryolite.Type;

public sealed class Concat
{
    public byte[] Buffer = new byte[64];

    public override bool Equals(object? obj) => obj is Concat c && this == c;
    public override int GetHashCode() => HashCodeHelper.CalculateHashCode(Buffer);
    public static implicit operator ReadOnlySpan<byte> (Concat concat) => concat.Buffer;
    public static bool operator ==(Concat x, Concat y) => x.Buffer.SequenceEqual(y.Buffer);
    public static bool operator !=(Concat x, Concat y) => !x.Buffer.SequenceEqual(y.Buffer);
}
