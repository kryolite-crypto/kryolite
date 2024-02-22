using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace Kryolite.Shared.Tests
{
    public class AddressTests
    {
        [Fact]
        public void AddressEqualityTest()
        {
            var addr1 = (Address)"kryo:acega4gtcwjfsjz5r9iwxduk62e9jhc5296naygg";
            var addr2 = (Address)"kryo:aa8e2p2u6ukjigyd9d3323d4sa5q3wc97g3278kj";
            var addr3 = (Address)"kryo:acega4gtcwjfsjz5r9iwxduk62e9jhc5296naygg";
            var addr4 = (Address)"kryo:aceekpv4jmegwd7css5ye7k9uinapp9kfef27je3";

            Assert.NotEqual(addr1, addr2);
            Assert.Equal(addr1, addr3);

            Assert.False(addr1 == addr2);
            Assert.True(addr1 == addr3);

            Assert.True(addr1 != addr2);
            Assert.False(addr1 != addr3);

            var set = new HashSet<Address>();
            set.Add(addr1);
            set.Add(addr2);

            Assert.Contains(addr3, set);
            Assert.DoesNotContain(addr4, set);

            // Check that adding existing value does not insert new value in set
            set.Add(addr3);
            Assert.Equal(2, set.Count);
        }
    }
}
