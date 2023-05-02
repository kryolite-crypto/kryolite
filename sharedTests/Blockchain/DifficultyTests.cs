using Xunit;
using Kryolite.Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Numerics;

namespace Kryolite.Shared.Tests
{
    public class DifficultyTests
    {
        [Theory]
        [InlineData(32U, 1)]
        [InlineData(35U, 1)]
        [InlineData(16384U, 1)]
        [InlineData(32166U, 1)]
        [InlineData(7356778676U, 1)]
        [InlineData(7356778676U, 7356778676U)]
        [InlineData(7356778676U * 2, 7356778676U * 2)]
        public void ToWorkTest(ulong val, ulong multiply)
        {
            var work = BigInteger.Multiply(new BigInteger(val), new BigInteger(multiply));
            var diff = BigInteger.Log(work, 2);
            var convertedDiff = BigInteger.Log(work.ToDifficulty().ToWork(), 2);

            var a = Math.Round(diff, 4, MidpointRounding.ToZero);
            var b = Math.Round(convertedDiff, 4, MidpointRounding.ToZero);

            Assert.Equal(a, b);
        }

        [Fact]
        public void ToStringTest()
        {
            var diff = new BigInteger(9785U).ToDifficulty();

            Assert.Equal("13.2563", diff.ToString());
        }
    }
}