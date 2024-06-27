using System.Numerics;
using System.Runtime.InteropServices;
using Kryolite.ByteSerializer;
using SimpleBase;

namespace Kryolite.Type.Tests;

public class SHA256HashTests
{
    [Fact]
    public void Constructor_ShouldInitializeBufferAndHashCode()
    {
        var sha256Hash = new SHA256Hash();

        Assert.NotNull(sha256Hash.Buffer);
        Assert.Equal(SHA256Hash.HASH_SZ, sha256Hash.Buffer.Length);
        Assert.Equal(SHA256Hash.NULL_HASH.GetHashCode(), sha256Hash.GetHashCode());
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_ForNullBuffer()
    {
        Assert.Throws<ArgumentNullException>(() => new SHA256Hash(null!));
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentOutOfRangeException_ForInvalidBufferSize()
    {
        var invalidBuffer = new byte[10];

        Assert.Throws<ArgumentOutOfRangeException>(() => new SHA256Hash(invalidBuffer));
    }

    [Fact]
    public void Constructor_ShouldInitializeBufferAndHashCode_ForValidBuffer()
    {
        var validBuffer = new byte[SHA256Hash.HASH_SZ];
        var sha256Hash = new SHA256Hash(validBuffer);

        Assert.Equal(validBuffer, sha256Hash.Buffer);
        Assert.Equal(HashCodeHelper.CalculateHashCode(validBuffer), sha256Hash.GetHashCode());
    }

    [Fact]
    public void ToString_ShouldReturnEncodedString()
    {
        var sha256Hash = new SHA256Hash();
        var result = sha256Hash.ToString();

        Assert.Equal(Base32.Bech32.Encode(sha256Hash.Buffer), result);
    }

    [Fact]
    public void ExplicitOperator_ShouldReturnBuffer()
    {
        var sha256Hash = new SHA256Hash();
        var buffer = (byte[])sha256Hash;

        Assert.Equal(sha256Hash.Buffer, buffer);
    }

    [Fact]
    public void ImplicitOperator_ShouldReturnReadOnlySpan()
    {
        var sha256Hash = new SHA256Hash();

        ReadOnlySpan<byte> span = sha256Hash;

        Assert.Equal(sha256Hash.Buffer, span.ToArray());
    }

    [Fact]
    public void ImplicitOperator_ShouldReturnSHA256Hash_FromBuffer()
    {
        var buffer = new byte[SHA256Hash.HASH_SZ];

        SHA256Hash sha256Hash = buffer;

        Assert.Equal(buffer, sha256Hash.Buffer);
    }

    [Fact]
    public void ImplicitOperator_ShouldReturnSHA256Hash_FromString()
    {
        var buffer = new byte[SHA256Hash.HASH_SZ];
        var encodedString = Base32.Bech32.Encode(buffer);

        SHA256Hash sha256Hash = encodedString;

        Assert.Equal(buffer, sha256Hash.Buffer);
    }

    [Fact]
    public void ImplicitOperator_ShouldReturnSHA256Hash_FromSpan()
    {
        var buffer = new byte[SHA256Hash.HASH_SZ];
        Span<byte> span = buffer;

        SHA256Hash sha256Hash = span;

        Assert.Equal(buffer, sha256Hash.Buffer);
    }

    [Fact]
    public void EqualityOperator_ShouldReturnTrue_ForEqualHashes()
    {
        var buffer = new byte[SHA256Hash.HASH_SZ];
        var hash1 = new SHA256Hash(buffer);
        var hash2 = new SHA256Hash(buffer);

        var result = hash1 == hash2;

        Assert.True(result);
    }

    [Fact]
    public void InequalityOperator_ShouldReturnFalse_ForEqualHashes()
    {
        var buffer = new byte[SHA256Hash.HASH_SZ];
        var hash1 = new SHA256Hash(buffer);
        var hash2 = new SHA256Hash(buffer);

        var result = hash1 != hash2;

        Assert.False(result);
    }

    [Fact]
    public void EqualityOperator_ShouldReturnFalse_ForDifferentHashes()
    {
        var buffer1 = new byte[SHA256Hash.HASH_SZ];
        var buffer2 = new byte[SHA256Hash.HASH_SZ];
        buffer2[0] = 1;
        var hash1 = new SHA256Hash(buffer1);
        var hash2 = new SHA256Hash(buffer2);

        var result = hash1 == hash2;

        Assert.False(result);
    }

    [Fact]
    public void InequalityOperator_ShouldReturnTrue_ForDifferentHashes()
    {
        var buffer1 = new byte[SHA256Hash.HASH_SZ];
        var buffer2 = new byte[SHA256Hash.HASH_SZ];
        buffer2[0] = 1;
        var hash1 = new SHA256Hash(buffer1);
        var hash2 = new SHA256Hash(buffer2);

        var result = hash1 != hash2;

        Assert.True(result);
    }

    [Fact]
    public void Equals_ShouldReturnTrue_ForEqualHashes()
    {
        var buffer = new byte[SHA256Hash.HASH_SZ];
        var hash1 = new SHA256Hash(buffer);
        var hash2 = new SHA256Hash(buffer);

        var result = hash1.Equals(hash2);

        Assert.True(result);
    }

    [Fact]
    public void Equals_ShouldReturnFalse_ForDifferentHashes()
    {
        var buffer1 = new byte[SHA256Hash.HASH_SZ];
        var buffer2 = new byte[SHA256Hash.HASH_SZ];
        buffer2[0] = 1;
        var hash1 = new SHA256Hash(buffer1);
        var hash2 = new SHA256Hash(buffer2);

        var result = hash1.Equals(hash2);

        Assert.False(result);
    }

    [Fact]
    public void GetHashCode_ShouldReturnSameHashCode_ForSameBuffer()
    {
        var buffer = new byte[SHA256Hash.HASH_SZ];
        var hash1 = new SHA256Hash(buffer);
        var hash2 = new SHA256Hash(buffer);

        var hashCode1 = hash1.GetHashCode();
        var hashCode2 = hash2.GetHashCode();

        Assert.Equal(hashCode1, hashCode2);
    }

    [Fact]
    public void GetSerializerId_ShouldReturnSHA256Enum()
    {
        var sha256Hash = new SHA256Hash();
        var serializerId = sha256Hash.GetSerializerId();

        Assert.Equal((byte)SerializerEnum.SHA256, serializerId);
    }

    [Fact]
    public void GetLength_ShouldReturnHashSize()
    {
        var sha256Hash = new SHA256Hash();
        var length = sha256Hash.GetLength();

        Assert.Equal(SHA256Hash.HASH_SZ, length);
    }

    [Fact]
    public void Serialize_ShouldWriteBufferToSerializer()
    {
        var buffer = new byte[SHA256Hash.HASH_SZ];
        Random.Shared.NextBytes(buffer);

        Span<byte> output = stackalloc byte[SHA256Hash.HASH_SZ];
        ref var spanRef = ref MemoryMarshal.GetReference(output);
        var serializer = new Serializer(ref spanRef, output.Length);

        var sha256Hash = new SHA256Hash(buffer);
        sha256Hash.Serialize(ref serializer);

        Assert.Equal(buffer, output.ToArray());
    }

    [Fact]
    public void Deserialize_ShouldReadBufferFromSerializer()
    {
        Span<byte> input = new byte[SHA256Hash.HASH_SZ];
        Random.Shared.NextBytes(input);

        ref var spanRef = ref MemoryMarshal.GetReference(input);
        var serializer = new Serializer(ref spanRef, input.Length);

        var sha256Hash = new SHA256Hash();
        sha256Hash.Deserialize(ref serializer);

        Assert.Equal(input.ToArray(), sha256Hash.Buffer);
    }

    [Fact]
    public void CompareTo_ShouldReturnZero_ForEqualHashes()
    {
        var buffer = new byte[SHA256Hash.HASH_SZ];
        var hash1 = new SHA256Hash(buffer);
        var hash2 = new SHA256Hash(buffer);

        var result = hash1.CompareTo(hash2);

        Assert.Equal(0, result);
    }

    [Fact]
    public void CompareTo_ShouldReturnNonZero_ForDifferentHashes()
    {
        var buffer1 = new byte[SHA256Hash.HASH_SZ];
        var buffer2 = new byte[SHA256Hash.HASH_SZ];
        buffer2[0] = 1;
        var hash1 = new SHA256Hash(buffer1);
        var hash2 = new SHA256Hash(buffer2);

        var result = hash1.CompareTo(hash2);

        Assert.NotEqual(0, result);
    }

    [Fact]
    public void ToBigInteger_ShouldReturnBigInteger_FromBuffer()
    {
        var buffer = new byte[SHA256Hash.HASH_SZ];
        var sha256Hash = new SHA256Hash(buffer);

        var bigInteger = sha256Hash.ToBigInteger();

        Assert.Equal(new BigInteger(buffer, true, true), bigInteger);
    }
}
