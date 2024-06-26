using Kryolite.Shared;
using Kryolite.Type;

namespace Kryolite.Node.Repository;

public interface IKeyRepository
{
    PublicKey GetPublicKey();
    PrivateKey GetPrivateKey();
}
