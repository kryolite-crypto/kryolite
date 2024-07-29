using Kryolite.Type;

namespace Kryolite.Module.Validator;

internal interface ISlots
{
    bool TryGetNextLeader(out Leader leader);
    void Ban(PublicKey publicKey);
    void Clear();
}
