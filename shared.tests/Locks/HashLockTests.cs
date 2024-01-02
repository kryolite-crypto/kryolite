using Kryolite.Shared.Locks;
using System.Security.Cryptography;
using Xunit;

namespace Kryolite.Shared.Tests;

public class HashLockTests
{
    [Fact]
    public void HashLock_ShouldLockPerHash()
    {
        var hashes = new List<SHA256Hash>(10);

        var hash = SHA256.HashData(BitConverter.GetBytes(1));

        // Add duplicate hashes
        for (var i = 0; i < 10; i++)
        {
            hashes.Add(hash);
        }

        var order = new List<int>();

        // We should evaluate all hashes sequentially since they are the same and we take lock on hash
        Parallel.For(0, 10, (i) => {
            using var _ = hashes[i].Lock();

            order.Add(i);
            Thread.Sleep(100);
            order.Add(i);
        });

        for (var i = 0; i < 10; i += 2)
        {
            Assert.Equal(hashes[i], hashes[i + 1]);
        }
    }

    [Fact]
    public void HashLock_ShouldRunDifferentHashesInParallel()
    {
        var hashes = new List<SHA256Hash>(10);

        // Add duplicate hashes
        for (var i = 0; i < 10; i++)
        {
            hashes.Add(SHA256.HashData(BitConverter.GetBytes(i)));
        }

        // We should evaluate all hashes parallel since they are different
        var barrier = new Barrier(10);

        var res = Parallel.For(0, 10, (i) => {
            using var _ = hashes[i].Lock();
            var ready = barrier.SignalAndWait(TimeSpan.FromSeconds(5));

            if (!ready)
            {
                Assert.Fail("Did not receive all participants.");
            }
        });
    }
}
