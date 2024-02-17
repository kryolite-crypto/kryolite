using System.Numerics;
using System.Text.Json.Serialization;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using Kryolite.Shared.Dto;

namespace Kryolite.Node;

[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    Converters = [
        typeof(AddressConverter),
        typeof(PrivateKeyConverter),
        typeof(PublicKeyConverter),
        typeof(SHA256HashConverter),
        typeof(SignatureConverter),
        typeof(DifficultyConverter),
        typeof(BigIntegerConverter),
    ]
)]
[JsonSerializable(typeof(ContractEvent))]
public partial class NodeSourceGenerationContext : JsonSerializerContext
{
}
