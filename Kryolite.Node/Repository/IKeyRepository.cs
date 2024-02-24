using Kryolite.Shared;

namespace Kryolite.Node.Repository;

public interface IKeyRepository
{
    Wallet GetKey();
}
