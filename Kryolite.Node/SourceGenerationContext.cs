using System.Text.Json.Serialization;
using Kryolite.Shared;

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
[JsonSerializable(typeof(GenericEventArgs))]
[JsonSerializable(typeof(ApprovalEventArgs))]
[JsonSerializable(typeof(ConsumeTokenEventArgs))]
[JsonSerializable(typeof(TransferTokenEventArgs))]
public partial class NodeSourceGenerationContext : JsonSerializerContext
{
}
