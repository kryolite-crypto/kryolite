namespace Kryolite.Type.Tests;

public class ConcatTests
{
    [Fact]
    public void Equals_ShouldReturnTrue_ForSameBufferContent()
    {
        var concat1 = new Concat { Buffer = new byte[] { 1, 2, 3 } };
        var concat2 = new Concat { Buffer = new byte[] { 1, 2, 3 } };

        var result = concat1.Equals(concat2);

        Assert.True(result);
    }

    [Fact]
    public void Equals_ShouldReturnFalse_ForDifferentBufferContent()
    {
        var concat1 = new Concat { Buffer = new byte[] { 1, 2, 3 } };
        var concat2 = new Concat { Buffer = new byte[] { 4, 5, 6 } };

        var result = concat1.Equals(concat2);

        Assert.False(result);
    }

    [Fact]
    public void GetHashCode_ShouldReturnSameHashCode_ForSameBufferContent()
    {
        var concat1 = new Concat { Buffer = new byte[] { 1, 2, 3 } };
        var concat2 = new Concat { Buffer = new byte[] { 1, 2, 3 } };

        var hashCode1 = concat1.GetHashCode();
        var hashCode2 = concat2.GetHashCode();

        Assert.Equal(hashCode1, hashCode2);
    }

    [Fact]
    public void GetHashCode_ShouldReturnDifferentHashCodes_ForDifferentBufferContent()
    {
        var concat1 = new Concat { Buffer = new byte[] { 1, 2, 3 } };
        var concat2 = new Concat { Buffer = new byte[] { 4, 5, 6 } };

        var hashCode1 = concat1.GetHashCode();
        var hashCode2 = concat2.GetHashCode();

        Assert.NotEqual(hashCode1, hashCode2);
    }

    [Fact]
    public void ImplicitConversion_ShouldReturnReadOnlySpan_WithSameBufferContent()
    {
        var concat = new Concat { Buffer = new byte[] { 1, 2, 3 } };

        ReadOnlySpan<byte> span = concat;

        Assert.Equal(concat.Buffer, span.ToArray());
    }

    [Fact]
    public void EqualityOperator_ShouldReturnTrue_ForSameBufferContent()
    {
        var concat1 = new Concat { Buffer = new byte[] { 1, 2, 3 } };
        var concat2 = new Concat { Buffer = new byte[] { 1, 2, 3 } };

        var result = concat1 == concat2;

        Assert.True(result);
    }

    [Fact]
    public void InequalityOperator_ShouldReturnFalse_ForSameBufferContent()
    {
        var concat1 = new Concat { Buffer = new byte[] { 1, 2, 3 } };
        var concat2 = new Concat { Buffer = new byte[] { 1, 2, 3 } };

        var result = concat1 != concat2;

        Assert.False(result);
    }

    [Fact]
    public void EqualityOperator_ShouldReturnFalse_ForDifferentBufferContent()
    {
        var concat1 = new Concat { Buffer = new byte[] { 1, 2, 3 } };
        var concat2 = new Concat { Buffer = new byte[] { 4, 5, 6 } };

        var result = concat1 == concat2;

        Assert.False(result);
    }

    [Fact]
    public void InequalityOperator_ShouldReturnTrue_ForDifferentBufferContent()
    {
        var concat1 = new Concat { Buffer = new byte[] { 1, 2, 3 } };
        var concat2 = new Concat { Buffer = new byte[] { 4, 5, 6 } };

        var result = concat1 != concat2;

        Assert.True(result);
    }
}
