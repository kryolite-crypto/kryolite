namespace Kryolite.Type.Tests;

public class HashCodeHelperTests
{
    [Fact]
    public void CalculateHashCode_ShouldReturnConsistentHashCode_ForSameInput()
    {
        var buffer = new byte[] { 1, 2, 3, 4, 5 };

        var hashCode1 = HashCodeHelper.CalculateHashCode(buffer);
        var hashCode2 = HashCodeHelper.CalculateHashCode(buffer);

        Assert.Equal(hashCode1, hashCode2);
    }

    [Fact]
    public void CalculateHashCode_ShouldReturnDifferentHashCodes_ForDifferentInputs()
    {
        var buffer1 = new byte[] { 1, 2, 3, 4, 5 };
        var buffer2 = new byte[] { 5, 4, 3, 2, 1 };

        var hashCode1 = HashCodeHelper.CalculateHashCode(buffer1);
        var hashCode2 = HashCodeHelper.CalculateHashCode(buffer2);

        Assert.NotEqual(hashCode1, hashCode2);
    }

    [Fact]
    public void CalculateHashCode_ShouldHandleEmptyBuffer()
    {
        var buffer = Array.Empty<byte>();

        var hashCode1 = HashCodeHelper.CalculateHashCode(buffer);
        var hashCode2 = HashCodeHelper.CalculateHashCode(buffer);

        Assert.Equal(hashCode1, hashCode2);
    }

    [Fact]
    public void CalculateHashCode_ShouldReturnSameHashCode_ForEquivalentSpanAndArray()
    {
        var bufferArray = new byte[] { 1, 2, 3, 4, 5 };
        var bufferSpan = new ReadOnlySpan<byte>(bufferArray);

        var hashCodeFromArray = HashCodeHelper.CalculateHashCode(bufferArray);
        var hashCodeFromSpan = HashCodeHelper.CalculateHashCode(bufferSpan);

        Assert.Equal(hashCodeFromArray, hashCodeFromSpan);
    }
}
