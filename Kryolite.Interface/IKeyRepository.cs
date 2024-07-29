using Kryolite.Type;

namespace Kryolite.Interface;

public interface IKeyRepository
{
    PublicKey GetPublicKey();
    PrivateKey GetPrivateKey();
}
