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
            var addr1 = (Address)"kryo:weai6vamhaawc23ijn9x8ju9ry5djyyy43nurcj4jw";
            var addr2 = (Address)"kryo:weahfksgx68n2vv77tgai3uxatitys2662mwx6ai6a";
            var addr3 = (Address)"kryo:weai6vamhaawc23ijn9x8ju9ry5djyyy43nurcj4jw";
            var addr4 = (Address)"kryo:weand2747q6pak2847yzsyx5r365ij58fat4adgnhw";

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
