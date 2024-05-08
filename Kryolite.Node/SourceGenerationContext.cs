using System.Text.Json.Serialization;
using Kryolite.Node.API;
using Kryolite.Shared;

namespace Kryolite.Node;

[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    IncludeFields = true,
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
[JsonSerializable(typeof(GenericEventArgs))]
[JsonSerializable(typeof(ApprovalEventArgs))]
[JsonSerializable(typeof(ConsumeTokenEventArgs))]
[JsonSerializable(typeof(TransferTokenEventArgs))]
[JsonSerializable(typeof(PeerStatsDto))]
[JsonSerializable(typeof(IEnumerable<PeerStatsDto>))]
public partial class NodeSourceGenerationContext : JsonSerializerContext
{
}
