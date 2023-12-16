using System.Diagnostics;
using Kryolite.Shared.Blockchain;
using Xunit;

namespace Kryolite.Shared.Tests;

public class GrasshopperTests
{
    [Theory]
    [InlineData(11, "ePeArgp7A3KeMF7DD7Dc2k2cygYFYDSaeQWW9iHRhRbu")]
    [InlineData(27, "6TTFwGnRR5Gpo6LoARU2o3H8HwyGiAszMVQE8CD2eXx2")]
    [InlineData(35, "76wsG6U5vB9PgWP6dbE58bNcBda13CeKJGLVtSNgRvZf")]
    [InlineData(42, "8ZUN5kdUuzQd5LArsDJ19vMuWixKyHT2sXd9jZWUN6kV")]
    [InlineData(69, "3azGoMeNrMNrufrJsVDVEowCkjTf9w9RnF7cMekWMtFa")]
    public void HashEquals(int seed, string expected)
    {
        var concat = new Concat();
        var rand = new Random(seed);

        rand.NextBytes(concat.Buffer);

        var hash = Grasshopper.Hash(concat);

        Assert.Equal(expected, hash);
    }

    [Fact]
    public void HashesShouldEqual()
    {
        var buffer = new byte [] { 184, 108, 15, 56, 200, 60, 242, 0, 192, 18, 149, 217, 120, 105, 115, 64, 238, 28, 240, 92, 77, 63, 152, 135, 220, 75, 27, 37, 212, 209, 79, 108, 222, 94, 24, 247, 194, 31, 104, 199, 47, 82, 244, 28, 112, 2, 156, 110, 92, 107, 133, 141, 246, 108, 121, 255, 191, 236, 170, 239, 201, 41, 113, 218 };

        var hash1 = Grasshopper.Hash(new Concat { Buffer = buffer });
        var hash2 = Grasshopper.Hash(new Concat { Buffer = buffer });

        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void HashesShouldNotEqualDifferentBuffer()
    {
        var buffer1 = new byte[] { 184, 108, 15, 56, 200, 60, 242, 0, 192, 18, 149, 217, 120, 105, 115, 64, 238, 28, 240, 92, 77, 63, 152, 135, 220, 75, 27, 37, 212, 209, 79, 108, 222, 94, 24, 247, 194, 31, 104, 199, 47, 82, 244, 28, 112, 2, 156, 110, 92, 107, 133, 141, 246, 108, 121, 255, 191, 236, 170, 239, 201, 41, 113, 218 };
        var buffer2 = new byte[] { 32, 33, 34, 11, 200, 60, 242, 0, 192, 18, 149, 217, 120, 105, 115, 64, 238, 28, 240, 92, 77, 63, 152, 135, 220, 75, 27, 37, 212, 209, 79, 108, 222, 94, 24, 247, 194, 31, 104, 199, 47, 82, 244, 28, 112, 2, 156, 110, 92, 107, 133, 141, 246, 108, 121, 255, 191, 236, 170, 239, 201, 41, 113, 218 };

        var hash1 = Grasshopper.Hash(new Concat { Buffer = buffer1 });
        var hash2 = Grasshopper.Hash(new Concat { Buffer = buffer2 });

        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void HashesShouldNotEqualDifferentParentHash()
    {
        var buffer = new byte[] { 184, 108, 15, 56, 200, 60, 242, 0, 192, 18, 149, 217, 120, 105, 115, 64, 238, 28, 240, 92, 77, 63, 152, 135, 220, 75, 27, 37, 212, 209, 79, 108, 222, 94, 24, 247, 194, 31, 104, 199, 47, 82, 244, 28, 112, 2, 156, 110, 92, 107, 133, 141, 246, 108, 121, 255, 191, 236, 170, 239, 201, 41, 113, 218 };

        var hash1 = Grasshopper.Hash(new Concat { Buffer = buffer });
        var hash2 = Grasshopper.Hash(new Concat { Buffer = buffer });

        Assert.NotEqual(hash1, hash2);
    }
}
