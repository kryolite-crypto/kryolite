using System.Runtime.InteropServices;
using Kryolite.ByteSerializer;
using SimpleBase;

namespace Kryolite.Type.Tests;

public class PrivateKeyTests
{
    [Fact]
    public void Constructor_ShouldInitializeBufferAndHashCode()
    {
        var privateKey = new PrivateKey();

        Assert.NotNull(privateKey.Buffer);
        Assert.Equal(PrivateKey.PRIVATE_KEY_SZ, privateKey.Buffer.Length);
        Assert.Equal(HashCodeHelper.CalculateHashCode(privateKey.Buffer), privateKey.GetHashCode());
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_ForNullBuffer()
    {
        Assert.Throws<ArgumentNullException>(() => new PrivateKey(null!));
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentOutOfRangeException_ForInvalidBufferSize()
    {
        var invalidBuffer = new byte[10];

        Assert.Throws<ArgumentOutOfRangeException>(() => new PrivateKey(invalidBuffer));
    }

    [Fact]
    public void Constructor_ShouldInitializeBufferAndHashCode_ForValidBuffer()
    {
        var validBuffer = new byte[PrivateKey.PRIVATE_KEY_SZ];

        var privateKey = new PrivateKey(validBuffer);

        Assert.Equal(validBuffer, privateKey.Buffer);
        Assert.Equal(HashCodeHelper.CalculateHashCode(validBuffer), privateKey.GetHashCode());
    }

    [Fact]
    public void ToString_ShouldReturnEncodedString()
    {
        var privateKey = new PrivateKey();

        var result = privateKey.ToString();

        Assert.Equal(Base32.Bech32.Encode(privateKey.Buffer), result);
    }

    [Fact]
    public void ExplicitOperator_ShouldReturnBuffer()
    {
        var privateKey = new PrivateKey();

        var buffer = (byte[])privateKey;

        Assert.Equal(privateKey.Buffer, buffer);
    }

    [Fact]
    public void ImplicitOperator_ShouldReturnReadOnlySpan()
    {
        var privateKey = new PrivateKey();

        ReadOnlySpan<byte> span = privateKey;

        Assert.Equal(privateKey.Buffer, span.ToArray());
    }

    [Fact]
    public void ImplicitOperator_ShouldReturnPrivateKey_FromBuffer()
    {
        var buffer = new byte[PrivateKey.PRIVATE_KEY_SZ];

        PrivateKey privateKey = buffer;

        Assert.Equal(buffer, privateKey.Buffer);
    }

    [Fact]
    public void ImplicitOperator_ShouldReturnPrivateKey_FromString()
    {
        var buffer = new byte[PrivateKey.PRIVATE_KEY_SZ];
        var encodedString = Base32.Bech32.Encode(buffer);

        PrivateKey privateKey = encodedString;

        Assert.Equal(buffer, privateKey.Buffer);
    }

    [Fact]
    public void EqualityOperator_ShouldReturnTrue_ForEqualKeys()
    {
        var buffer = new byte[PrivateKey.PRIVATE_KEY_SZ];
        var key1 = new PrivateKey(buffer);
        var key2 = new PrivateKey(buffer);

        var result = key1 == key2;

        Assert.True(result);
    }

    [Fact]
    public void InequalityOperator_ShouldReturnFalse_ForEqualKeys()
    {
        var buffer = new byte[PrivateKey.PRIVATE_KEY_SZ];
        var key1 = new PrivateKey(buffer);
        var key2 = new PrivateKey(buffer);

        var result = key1 != key2;

        Assert.False(result);
    }

    [Fact]
    public void EqualityOperator_ShouldReturnFalse_ForDifferentKeys()
    {
        var buffer1 = new byte[PrivateKey.PRIVATE_KEY_SZ];
        var buffer2 = new byte[PrivateKey.PRIVATE_KEY_SZ];
        buffer2[0] = 1;
        var key1 = new PrivateKey(buffer1);
        var key2 = new PrivateKey(buffer2);

        var result = key1 == key2;

        Assert.False(result);
    }

    [Fact]
    public void InequalityOperator_ShouldReturnTrue_ForDifferentKeys()
    {
        var buffer1 = new byte[PrivateKey.PRIVATE_KEY_SZ];
        var buffer2 = new byte[PrivateKey.PRIVATE_KEY_SZ];
        buffer2[0] = 1;
        var key1 = new PrivateKey(buffer1);
        var key2 = new PrivateKey(buffer2);

        var result = key1 != key2;

        Assert.True(result);
    }

    [Fact]
    public void Equals_ShouldReturnTrue_ForEqualKeys()
    {
        var buffer = new byte[PrivateKey.PRIVATE_KEY_SZ];
        var key1 = new PrivateKey(buffer);
        var key2 = new PrivateKey(buffer);

        var result = key1.Equals(key2);

        Assert.True(result);
    }

    [Fact]
    public void Equals_ShouldReturnFalse_ForDifferentKeys()
    {
        var buffer1 = new byte[PrivateKey.PRIVATE_KEY_SZ];
        var buffer2 = new byte[PrivateKey.PRIVATE_KEY_SZ];
        buffer2[0] = 1;
        var key1 = new PrivateKey(buffer1);
        var key2 = new PrivateKey(buffer2);

        var result = key1.Equals(key2);

        Assert.False(result);
    }

    [Fact]
    public void GetHashCode_ShouldReturnSameHashCode_ForSameBuffer()
    {
        var buffer = new byte[PrivateKey.PRIVATE_KEY_SZ];
        var key1 = new PrivateKey(buffer);
        var key2 = new PrivateKey(buffer);

        var hashCode1 = key1.GetHashCode();
        var hashCode2 = key2.GetHashCode();

        Assert.Equal(hashCode1, hashCode2);
    }

    [Fact]
    public void GetSerializerId_ShouldReturnPrivateKeyEnum()
    {
        var privateKey = new PrivateKey();
        var serializerId = privateKey.GetSerializerId();

        Assert.Equal((byte)SerializerEnum.PRIVATE_KEY, serializerId);
    }

    [Fact]
    public void GetLength_ShouldReturnPrivateKeySize()
    {
        var privateKey = new PrivateKey();
        var length = privateKey.GetLength();

        Assert.Equal(PrivateKey.PRIVATE_KEY_SZ, length);
    }

    [Fact]
    public void Serialize_ShouldWriteBufferToSerializer()
    {
        var buffer = new byte[PrivateKey.PRIVATE_KEY_SZ];
        Random.Shared.NextBytes(buffer);

        Span<byte> output = stackalloc byte[PrivateKey.PRIVATE_KEY_SZ];
        ref var spanRef = ref MemoryMarshal.GetReference(output);
        var serializer = new Serializer(ref spanRef, output.Length);

        var privateKey = new PrivateKey(buffer);
        privateKey.Serialize(ref serializer);

        Assert.Equal(buffer, output.ToArray());
    }

    [Fact]
    public void Deserialize_ShouldReadBufferFromSerializer()
    {
        Span<byte> input = new byte[PrivateKey.PRIVATE_KEY_SZ];
        Random.Shared.NextBytes(input);

        ref var spanRef = ref MemoryMarshal.GetReference(input);
        var serializer = new Serializer(ref spanRef, input.Length);

        var privateKey = new PrivateKey();
        privateKey.Deserialize(ref serializer);

        Assert.Equal(input.ToArray(), privateKey.Buffer);
    }
}
