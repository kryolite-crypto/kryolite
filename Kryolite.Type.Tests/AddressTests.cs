using System.Runtime.InteropServices;
using Kryolite.ByteSerializer;
using SimpleBase;

namespace Kryolite.Type.Tests;

public class AddressTests
{
    [Fact]
    public void Constructor_ShouldInitializeBufferAndHashCode()
    {
        var address = new Address();

        Assert.NotNull(address.Buffer);
        Assert.Equal(Address.ADDRESS_SZ, address.Buffer.Length);
        Assert.Equal(Address.NULL_ADDRESS.GetHashCode(), address.GetHashCode());
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_ForNullBuffer()
    {
        Assert.Throws<ArgumentNullException>(() => new Address(null!));
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentOutOfRangeException_ForInvalidBufferSize()
    {
        var invalidBuffer = new byte[10];

        Assert.Throws<ArgumentOutOfRangeException>(() => new Address(invalidBuffer));
    }

    [Fact]
    public void Constructor_ShouldInitializeBufferAndHashCode_ForValidBuffer()
    {
        var validBuffer = new byte[Address.ADDRESS_SZ];
        var address = new Address(validBuffer);

        Assert.Equal(validBuffer, address.Buffer);
        Assert.Equal(HashCodeHelper.CalculateHashCode(validBuffer), address.GetHashCode());
    }

    [Fact]
    public void IsContract_ShouldReturnTrue_ForContractAddress()
    {
        var buffer = new byte[Address.ADDRESS_SZ];
        buffer[0] = (byte)AddressType.CONTRACT;
        var address = new Address(buffer);

        var result = address.IsContract();

        Assert.True(result);
    }

    [Fact]
    public void IsContract_ShouldReturnFalse_ForNonContractAddress()
    {
        var buffer = new byte[Address.ADDRESS_SZ];
        buffer[0] = (byte)AddressType.WALLET;
        var address = new Address(buffer);

        var result = address.IsContract();

        Assert.False(result);
    }

    [Fact]
    public void IsWallet_ShouldReturnTrue_ForWalletAddress()
    {
        var buffer = new byte[Address.ADDRESS_SZ];
        buffer[0] = (byte)AddressType.WALLET;
        var address = new Address(buffer);

        var result = address.IsWallet();

        Assert.True(result);
    }

    [Fact]
    public void IsWallet_ShouldReturnFalse_ForNonWalletAddress()
    {
        var buffer = new byte[Address.ADDRESS_SZ];
        buffer[0] = (byte)AddressType.CONTRACT;
        var address = new Address(buffer);

        var result = address.IsWallet();

        Assert.False(result);
    }

    [Fact]
    public void ToString_ShouldReturnEncodedString()
    {
        var address = new Address();
        var result = address.ToString();

        Assert.Equal(Address.ADDR_PREFIX + Base32.Bech32.Encode(address.Buffer), result);
    }

    [Fact]
    public void ExplicitOperator_ShouldReturnBuffer()
    {
        var address = new Address();
        var buffer = (byte[])address;

        Assert.Equal(address.Buffer, buffer);
    }

    [Fact]
    public void ImplicitOperator_ShouldReturnReadOnlySpan()
    {
        var address = new Address();

        ReadOnlySpan<byte> readOnlySpan = address;

        Assert.Equal(address.Buffer, readOnlySpan.ToArray());
    }

    [Fact]
    public void ImplicitOperator_ShouldReturnAddress_FromBuffer()
    {
        var buffer = new byte[Address.ADDRESS_SZ];

        Address address = buffer;

        Assert.Equal(buffer, address.Buffer);
    }

    [Fact]
    public void ImplicitOperator_ShouldReturnAddress_FromString()
    {
        var buffer = new byte[Address.ADDRESS_SZ];
        var encodedString = Address.ADDR_PREFIX + Base32.Bech32.Encode(buffer);

        Address address = encodedString;

        Assert.Equal(buffer, address.Buffer);
    }

    [Fact]
    public void EqualityOperator_ShouldReturnTrue_ForEqualAddresses()
    {
        var buffer = new byte[Address.ADDRESS_SZ];
        var addr1 = new Address(buffer);
        var addr2 = new Address(buffer);

        var result = addr1 == addr2;

        Assert.True(result);
    }

    [Fact]
    public void InequalityOperator_ShouldReturnFalse_ForEqualAddresses()
    {
        var buffer = new byte[Address.ADDRESS_SZ];
        var addr1 = new Address(buffer);
        var addr2 = new Address(buffer);

        var result = addr1 != addr2;

        Assert.False(result);
    }

    [Fact]
    public void EqualityOperator_ShouldReturnFalse_ForDifferentAddresses()
    {
        var buffer1 = new byte[Address.ADDRESS_SZ];
        var buffer2 = new byte[Address.ADDRESS_SZ];
        buffer2[0] = 1;
        var addr1 = new Address(buffer1);
        var addr2 = new Address(buffer2);

        var result = addr1 == addr2;

        Assert.False(result);
    }

    [Fact]
    public void InequalityOperator_ShouldReturnTrue_ForDifferentAddresses()
    {
        var buffer1 = new byte[Address.ADDRESS_SZ];
        var buffer2 = new byte[Address.ADDRESS_SZ];
        buffer2[0] = 1;
        var addr1 = new Address(buffer1);
        var addr2 = new Address(buffer2);

        var result = addr1 != addr2;

        Assert.True(result);
    }

    [Fact]
    public void Equals_ShouldReturnTrue_ForEqualAddresses()
    {
        var buffer = new byte[Address.ADDRESS_SZ];
        var addr1 = new Address(buffer);
        var addr2 = new Address(buffer);

        var result = addr1.Equals(addr2);

        Assert.True(result);
    }

    [Fact]
    public void Equals_ShouldReturnFalse_ForDifferentAddresses()
    {
        var buffer1 = new byte[Address.ADDRESS_SZ];
        var buffer2 = new byte[Address.ADDRESS_SZ];
        buffer2[0] = 1;
        var addr1 = new Address(buffer1);
        var addr2 = new Address(buffer2);

        var result = addr1.Equals(addr2);

        Assert.False(result);
    }

    [Fact]
    public void GetHashCode_ShouldReturnSameHashCode_ForSameBuffer()
    {
        var buffer = new byte[Address.ADDRESS_SZ];
        var addr1 = new Address(buffer);
        var addr2 = new Address(buffer);

        var hashCode1 = addr1.GetHashCode();
        var hashCode2 = addr2.GetHashCode();

        Assert.Equal(hashCode1, hashCode2);
    }

    [Fact]
    public void Create_ShouldReturnValidAddress_ForPublicKey()
    {
        var publicKeyBuffer = new byte[PublicKey.PUB_KEY_SZ];
        var publicKey = new PublicKey(publicKeyBuffer);

        var address = Address.Create(publicKey);

        Assert.True(Address.IsValid(address.ToString()));
    }

    [Fact]
    public void IsValid_ShouldReturnFalse_ForInvalidPrefix()
    {
        var invalidAddress = "invalidPrefix:abcdefg";
        var result = Address.IsValid(invalidAddress);

        Assert.False(result);
    }

    [Fact]
    public void IsValid_ShouldReturnFalse_ForInvalidLength()
    {
        var buffer = new byte[10];
        var invalidAddress = Address.ADDR_PREFIX + Base32.Bech32.Encode(buffer);

        var result = Address.IsValid(invalidAddress);

        Assert.False(result);
    }

    [Fact]
    public void IsValid_ShouldReturnFalse_ForInvalidChecksum()
    {
        var buffer = new byte[PublicKey.PUB_KEY_SZ];
        Random.Shared.NextBytes(buffer);
        var pubKey = new PublicKey(buffer);
        var address = Address.Create(pubKey);

        buffer = address.Buffer;
        buffer[^1] = 1; // Modify checksum to be invalid
        var invalidAddress = Address.ADDR_PREFIX + Base32.Bech32.Encode(buffer);

        var result = Address.IsValid(invalidAddress);

        Assert.False(result);
    }

    [Fact]
    public void IsValid_ShouldReturnTrue_ForValidAddress()
    {
        var buffer = new byte[PublicKey.PUB_KEY_SZ];
        Random.Shared.NextBytes(buffer);
        var pubKey = new PublicKey(buffer);
        var address = Address.Create(pubKey);

        var result = Address.IsValid(address.ToString());

        Assert.True(result);
    }

    [Fact]
    public void GetSerializerId_ShouldReturnAddressEnum()
    {
        var address = new Address();
        var serializerId = address.GetSerializerId();

        Assert.Equal((byte)SerializerEnum.ADDRESS, serializerId);
    }

    [Fact]
    public void GetLength_ShouldReturnAddressSize()
    {
        var address = new Address();
        var length = address.GetLength();

        Assert.Equal(Address.ADDRESS_SZ, length);
    }

    [Fact]
    public void Serialize_ShouldWriteBufferToSerializer()
    {
        var buffer = new byte[Address.ADDRESS_SZ];
        Random.Shared.NextBytes(buffer);

        Span<byte> output = stackalloc byte[Address.ADDRESS_SZ];
        ref var spanRef = ref MemoryMarshal.GetReference(output);
        var serializer = new Serializer(ref spanRef, output.Length);

        var address = new Address(buffer);
        address.Serialize(ref serializer);

        Assert.Equal(buffer, output.ToArray());
    }

    [Fact]
    public void Deserialize_ShouldReadBufferFromSerializer()
    {
        Span<byte> input = new byte[Address.ADDRESS_SZ];
        Random.Shared.NextBytes(input);

        ref var spanRef = ref MemoryMarshal.GetReference(input);
        var serializer = new Serializer(ref spanRef, input.Length);

        var address = new Address();
        address.Deserialize(ref serializer);

        Assert.Equal(input.ToArray(), address.Buffer);
        
        // Should update HashCode
        Assert.NotEqual(address.GetHashCode(), Address.NULL_ADDRESS.GetHashCode());
    }
}
