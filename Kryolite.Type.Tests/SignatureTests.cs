using System.Runtime.InteropServices;
using Kryolite.ByteSerializer;
using SimpleBase;

namespace Kryolite.Type.Tests;

public class SignatureTests
{
    [Fact]
    public void Constructor_ShouldInitializeBufferAndHashCode()
    {
        var signature = new Signature();

        Assert.NotNull(signature.Buffer);
        Assert.Equal(Signature.SIGNATURE_SZ, signature.Buffer.Length);
        Assert.Equal(Signature.NULL_SIGNATURE.GetHashCode(), signature.GetHashCode());
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_ForNullBuffer()
    {
        Assert.Throws<ArgumentNullException>(() => new Signature(null!));
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentOutOfRangeException_ForInvalidBufferSize()
    {
        var invalidBuffer = new byte[10];

        Assert.Throws<ArgumentOutOfRangeException>(() => new Signature(invalidBuffer));
    }

    [Fact]
    public void Constructor_ShouldInitializeBufferAndHashCode_ForValidBuffer()
    {
        var validBuffer = new byte[Signature.SIGNATURE_SZ];
        var signature = new Signature(validBuffer);

        Assert.Equal(validBuffer, signature.Buffer);
        Assert.Equal(HashCodeHelper.CalculateHashCode(validBuffer), signature.GetHashCode());
    }

    [Fact]
    public void ToString_ShouldReturnEncodedString()
    {
        var signature = new Signature();
        var result = signature.ToString();

        Assert.Equal(Base32.Bech32.Encode(signature.Buffer), result);
    }

    [Fact]
    public void ExplicitOperator_ShouldReturnBuffer()
    {
        var signature = new Signature();
        var buffer = (byte[])signature;

        Assert.Equal(signature.Buffer, buffer);
    }

    [Fact]
    public void ImplicitOperator_ShouldReturnSpan()
    {
        var signature = new Signature();

        Span<byte> span = signature;

        Assert.Equal(signature.Buffer, span.ToArray());
    }

    [Fact]
    public void ImplicitOperator_ShouldReturnReadOnlySpan()
    {
        var signature = new Signature();

        ReadOnlySpan<byte> readOnlySpan = signature;

        Assert.Equal(signature.Buffer, readOnlySpan.ToArray());
    }

    [Fact]
    public void ImplicitOperator_ShouldReturnSignature_FromBuffer()
    {
        var buffer = new byte[Signature.SIGNATURE_SZ];

        Signature signature = buffer;

        Assert.Equal(buffer, signature.Buffer);
    }

    [Fact]
    public void ImplicitOperator_ShouldReturnSignature_FromString()
    {
        var buffer = new byte[Signature.SIGNATURE_SZ];
        var encodedString = Base32.Bech32.Encode(buffer);

        Signature signature = encodedString;

        Assert.Equal(buffer, signature.Buffer);
    }

    [Fact]
    public void EqualityOperator_ShouldReturnTrue_ForEqualSignatures()
    {
        var buffer = new byte[Signature.SIGNATURE_SZ];
        var sig1 = new Signature(buffer);
        var sig2 = new Signature(buffer);

        var result = sig1 == sig2;

        Assert.True(result);
    }

    [Fact]
    public void InequalityOperator_ShouldReturnFalse_ForEqualSignatures()
    {
        var buffer = new byte[Signature.SIGNATURE_SZ];
        var sig1 = new Signature(buffer);
        var sig2 = new Signature(buffer);

        var result = sig1 != sig2;

        Assert.False(result);
    }

    [Fact]
    public void EqualityOperator_ShouldReturnFalse_ForDifferentSignatures()
    {
        var buffer1 = new byte[Signature.SIGNATURE_SZ];
        var buffer2 = new byte[Signature.SIGNATURE_SZ];
        buffer2[0] = 1;
        var sig1 = new Signature(buffer1);
        var sig2 = new Signature(buffer2);

        var result = sig1 == sig2;

        Assert.False(result);
    }

    [Fact]
    public void InequalityOperator_ShouldReturnTrue_ForDifferentSignatures()
    {
        var buffer1 = new byte[Signature.SIGNATURE_SZ];
        var buffer2 = new byte[Signature.SIGNATURE_SZ];
        buffer2[0] = 1;
        var sig1 = new Signature(buffer1);
        var sig2 = new Signature(buffer2);

        var result = sig1 != sig2;

        Assert.True(result);
    }

    [Fact]
    public void Equals_ShouldReturnTrue_ForEqualSignatures()
    {
        var buffer = new byte[Signature.SIGNATURE_SZ];
        var sig1 = new Signature(buffer);
        var sig2 = new Signature(buffer);

        var result = sig1.Equals(sig2);

        Assert.True(result);
    }

    [Fact]
    public void Equals_ShouldReturnFalse_ForDifferentSignatures()
    {
        var buffer1 = new byte[Signature.SIGNATURE_SZ];
        var buffer2 = new byte[Signature.SIGNATURE_SZ];
        buffer2[0] = 1;
        var sig1 = new Signature(buffer1);
        var sig2 = new Signature(buffer2);

        var result = sig1.Equals(sig2);

        Assert.False(result);
    }

    [Fact]
    public void GetHashCode_ShouldReturnSameHashCode_ForSameBuffer()
    {
        var buffer = new byte[Signature.SIGNATURE_SZ];
        var sig1 = new Signature(buffer);
        var sig2 = new Signature(buffer);

        var hashCode1 = sig1.GetHashCode();
        var hashCode2 = sig2.GetHashCode();

        Assert.Equal(hashCode1, hashCode2);
    }

    [Fact]
    public void GetSerializerId_ShouldReturnSignatureEnum()
    {
        var signature = new Signature();
        var serializerId = signature.GetSerializerId();

        Assert.Equal((byte)SerializerEnum.SIGNATURE, serializerId);
    }

    [Fact]
    public void GetLength_ShouldReturnSignatureSize()
    {
        var signature = new Signature();
        var length = signature.GetLength();

        Assert.Equal(Signature.SIGNATURE_SZ, length);
    }

    [Fact]
    public void Serialize_ShouldWriteBufferToSerializer()
    {
        var buffer = new byte[Signature.SIGNATURE_SZ];
        Random.Shared.NextBytes(buffer);

        Span<byte> output = stackalloc byte[Signature.SIGNATURE_SZ];
        ref var spanRef = ref MemoryMarshal.GetReference(output);
        var serializer = new Serializer(ref spanRef, output.Length);

        var signature = new Signature(buffer);
        signature.Serialize(ref serializer);

        Assert.Equal(buffer, output.ToArray());
    }

    [Fact]
    public void Deserialize_ShouldReadBufferFromSerializer()
    {
        Span<byte> input = stackalloc byte[Signature.SIGNATURE_SZ];
        Random.Shared.NextBytes(input);
    
        ref var spanRef = ref MemoryMarshal.GetReference(input);
        var serializer = new Serializer(ref spanRef, input.Length);

        var signature = new Signature();
        signature.Deserialize(ref serializer);

        Assert.Equal(input.ToArray(), signature.Buffer);
    }

    [Fact]
    public void CompareTo_ShouldReturnZero_ForEqualSignatures()
    {
        // Arrange
        var buffer = new byte[Signature.SIGNATURE_SZ];
        var sig1 = new Signature(buffer);
        var sig2 = new Signature(buffer);

        // Act
        var result = sig1.CompareTo(sig2);

        // Assert
        Assert.Equal(0, result);
    }

    [Fact]
    public void CompareTo_ShouldReturnNonZero_ForDifferentSignatures()
    {
        // Arrange
        var buffer1 = new byte[Signature.SIGNATURE_SZ];
        var buffer2 = new byte[Signature.SIGNATURE_SZ];
        buffer2[0] = 1;
        var sig1 = new Signature(buffer1);
        var sig2 = new Signature(buffer2);

        // Act
        var result = sig1.CompareTo(sig2);

        // Assert
        Assert.NotEqual(0, result);
    }
}
