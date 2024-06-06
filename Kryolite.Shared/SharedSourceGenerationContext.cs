using System.Numerics;
using System.Text.Json.Serialization;
using Kryolite.Shared.Blockchain;
using Kryolite.Shared.Dto;

namespace Kryolite.Shared;

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
[JsonSerializable(typeof(BlockTemplate))]
[JsonSerializable(typeof(CallMethod))]
[JsonSerializable(typeof(Contract))]
[JsonSerializable(typeof(ContractManifest))]
[JsonSerializable(typeof(GithubRelease))]
[JsonSerializable(typeof(Ledger))]
[JsonSerializable(typeof(Token))]
[JsonSerializable(typeof(Validator))]
[JsonSerializable(typeof(ChainStateDto))]
[JsonSerializable(typeof(BlockDto))]
[JsonSerializable(typeof(ViewDto))]
[JsonSerializable(typeof(VoteDto))]
[JsonSerializable(typeof(Transaction))]
[JsonSerializable(typeof(TransactionDto))]
[JsonSerializable(typeof(TransactionDtoEx))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(List<Token>))]
[JsonSerializable(typeof(List<Transaction>))]
[JsonSerializable(typeof(List<TransactionDto>))]
[JsonSerializable(typeof(List<TransactionStatusDto>))]
[JsonSerializable(typeof(List<Validator>))]
[JsonSerializable(typeof(List<SHA256Hash>))]
[JsonSerializable(typeof(List<Effect>))]
[JsonSerializable(typeof(IEnumerable<string>))]
[JsonSerializable(typeof(IEnumerable<NodeDto>))]
[JsonSerializable(typeof(IEnumerable<NodeDtoEx>))]
[JsonSerializable(typeof(IEnumerable<TransactionDtoEx>))]
[JsonSerializable(typeof(IEnumerable<WalletBalanceDto>))]
[JsonSerializable(typeof(BigInteger))]
[JsonSerializable(typeof(SHA256Hash))]
[JsonSerializable(typeof(Address))]
[JsonSerializable(typeof(PublicKey))]
[JsonSerializable(typeof(Signature))]
[JsonSerializable(typeof(Difficulty))]
[JsonSerializable(typeof(ExecutionResult))]
[JsonSerializable(typeof(TransactionType))]
[JsonSerializable(typeof(HistoryData))]
public partial class SharedSourceGenerationContext : JsonSerializerContext
{
}
