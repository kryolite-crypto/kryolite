using Kryolite.Shared.Blockchain;

namespace Kryolite.Node;

public interface IVerifier
{
    bool Verify(Transaction tx);
    bool Verify(Block block);
    bool Verify(Vote vote);
    bool Verify(View view);
}
