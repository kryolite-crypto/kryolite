using Kryolite.Shared;

namespace Kryolite.Node.Repository;

public interface IKeyRepository
{
    PublicKey GetPublicKey();
    PrivateKey GetPrivateKey();
}
