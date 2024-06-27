using System.Runtime.InteropServices;
using Kryolite.ByteSerializer;
using SimpleBase;

namespace Kryolite.Type.Tests;

public class PublicKeyTests
{
    [Fact]
    public void Constructor_ShouldInitializeBufferAndHashCode()
    {
        var publicKey = new PublicKey();

        Assert.NotNull(publicKey.Buffer);
        Assert.Equal(PublicKey.PUB_KEY_SZ, publicKey.Buffer.Length);
        Assert.Equal(PublicKey.NULL_PUBLIC_KEY.GetHashCode(), publicKey.GetHashCode());
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_ForNullBuffer()
    {
        Assert.Throws<ArgumentNullException>(() => new PublicKey(null!));
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentOutOfRangeException_ForInvalidBufferSize()
    {
        var invalidBuffer = new byte[10];

        Assert.Throws<ArgumentOutOfRangeException>(() => new PublicKey(invalidBuffer));
    }

    [Fact]
    public void Constructor_ShouldInitializeBufferAndHashCode_ForValidBuffer()
    {
        var validBuffer = new byte[PublicKey.PUB_KEY_SZ];
        var publicKey = new PublicKey(validBuffer);

        Assert.Equal(validBuffer, publicKey.Buffer);
        Assert.Equal(HashCodeHelper.CalculateHashCode(validBuffer), publicKey.GetHashCode());
    }

    [Fact]
    public void ToString_ShouldReturnEncodedString()
    {
        var publicKey = new PublicKey();
        var result = publicKey.ToString();

        Assert.Equal(Base32.Bech32.Encode(publicKey.Buffer), result);
    }

    [Fact]
    public void ExplicitOperator_ShouldReturnBuffer()
    {
        var publicKey = new PublicKey();
        var buffer = (byte[])publicKey;

        Assert.Equal(publicKey.Buffer, buffer);
    }

    [Fact]
    public void ImplicitOperator_ShouldReturnReadOnlySpan()
    {
        var publicKey = new PublicKey();

        ReadOnlySpan<byte> span = publicKey;

        Assert.Equal(publicKey.Buffer, span.ToArray());
    }

    [Fact]
    public void ImplicitOperator_ShouldReturnPublicKey_FromBuffer()
    {
        var buffer = new byte[PublicKey.PUB_KEY_SZ];

        PublicKey publicKey = buffer;

        Assert.Equal(buffer, publicKey.Buffer);
    }

    [Fact]
    public void ImplicitOperator_ShouldReturnPublicKey_FromString()
    {
        var buffer = new byte[PublicKey.PUB_KEY_SZ];
        var encodedString = Base32.Bech32.Encode(buffer);

        PublicKey publicKey = encodedString;

        Assert.Equal(buffer, publicKey.Buffer);
    }

    [Fact]
    public void EqualityOperator_ShouldReturnTrue_ForEqualKeys()
    {
        var buffer = new byte[PublicKey.PUB_KEY_SZ];
        var key1 = new PublicKey(buffer);
        var key2 = new PublicKey(buffer);

        var result = key1 == key2;

        Assert.True(result);
    }

    [Fact]
    public void InequalityOperator_ShouldReturnFalse_ForEqualKeys()
    {
        var buffer = new byte[PublicKey.PUB_KEY_SZ];
        var key1 = new PublicKey(buffer);
        var key2 = new PublicKey(buffer);

        var result = key1 != key2;

        Assert.False(result);
    }

    [Fact]
    public void EqualityOperator_ShouldReturnFalse_ForDifferentKeys()
    {
        var buffer1 = new byte[PublicKey.PUB_KEY_SZ];
        var buffer2 = new byte[PublicKey.PUB_KEY_SZ];
        buffer2[0] = 1;
        var key1 = new PublicKey(buffer1);
        var key2 = new PublicKey(buffer2);

        var result = key1 == key2;

        Assert.False(result);
    }

    [Fact]
    public void InequalityOperator_ShouldReturnTrue_ForDifferentKeys()
    {
        var buffer1 = new byte[PublicKey.PUB_KEY_SZ];
        var buffer2 = new byte[PublicKey.PUB_KEY_SZ];
        buffer2[0] = 1;
        var key1 = new PublicKey(buffer1);
        var key2 = new PublicKey(buffer2);

        var result = key1 != key2;

        Assert.True(result);
    }

    [Fact]
    public void Equals_ShouldReturnTrue_ForEqualKeys()
    {
        var buffer = new byte[PublicKey.PUB_KEY_SZ];
        var key1 = new PublicKey(buffer);
        var key2 = new PublicKey(buffer);

        var result = key1.Equals(key2);

        Assert.True(result);
    }

    [Fact]
    public void Equals_ShouldReturnFalse_ForDifferentKeys()
    {
        var buffer1 = new byte[PublicKey.PUB_KEY_SZ];
        var buffer2 = new byte[PublicKey.PUB_KEY_SZ];
        buffer2[0] = 1;
        var key1 = new PublicKey(buffer1);
        var key2 = new PublicKey(buffer2);

        var result = key1.Equals(key2);

        Assert.False(result);
    }

    [Fact]
    public void GetHashCode_ShouldReturnSameHashCode_ForSameBuffer()
    {
        var buffer = new byte[PublicKey.PUB_KEY_SZ];
        var key1 = new PublicKey(buffer);
        var key2 = new PublicKey(buffer);

        var hashCode1 = key1.GetHashCode();
        var hashCode2 = key2.GetHashCode();

        Assert.Equal(hashCode1, hashCode2);
    }

    [Fact]
    public void GetSerializerId_ShouldReturnPublicKeyEnum()
    {
        var publicKey = new PublicKey();

        var serializerId = publicKey.GetSerializerId();

        Assert.Equal((byte)SerializerEnum.PUBLIC_KEY, serializerId);
    }

    [Fact]
    public void GetLength_ShouldReturnPublicKeySize()
    {
        // Arrange
        var publicKey = new PublicKey();

        // Act
        var length = publicKey.GetLength();

        // Assert
        Assert.Equal(PublicKey.PUB_KEY_SZ, length);
    }

    [Fact]
    public void Serialize_ShouldWriteBufferToSerializer()
    {
        var buffer = new byte[PublicKey.PUB_KEY_SZ];
        Random.Shared.NextBytes(buffer);

        Span<byte> output = stackalloc byte[PublicKey.PUB_KEY_SZ];
        ref var spanRef = ref MemoryMarshal.GetReference(output);
        var serializer = new Serializer(ref spanRef, output.Length);

        var publicKey = new PublicKey(buffer);
        publicKey.Serialize(ref serializer);

        Assert.Equal(buffer, output.ToArray());
    }

    [Fact]
    public void Deserialize_ShouldReadBufferFromSerializer()
    {
        Span<byte> input = stackalloc byte[PublicKey.PUB_KEY_SZ];
        Random.Shared.NextBytes(input);

        ref var spanRef = ref MemoryMarshal.GetReference(input);
        var serializer = new Serializer(ref spanRef, input.Length);

        var publicKey = new PublicKey();
        publicKey.Deserialize(ref serializer);

        Assert.Equal(input.ToArray(), publicKey.Buffer);
    }

    [Fact]
    public void ToAddress_ShouldReturnAddress_FromBuffer()
    {
        var buffer = new byte[PublicKey.PUB_KEY_SZ];
        Random.Shared.NextBytes(buffer);

        var publicKey = new PublicKey(buffer);
        var address = publicKey.ToAddress();

        Assert.NotNull(address);
        Assert.Equal(Address.Create(buffer), address);
    }
}
