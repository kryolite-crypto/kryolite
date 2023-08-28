using Kryolite.Shared.Blockchain;

namespace Kryolite.Node;

public interface IVerifier
{
    bool Verify(Transaction tx);
}