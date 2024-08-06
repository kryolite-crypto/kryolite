using System.Collections.Immutable;
using Kryolite.Type;

namespace Kryolite;

public static class Constant
{
    public const string STORE_VERSION = "9";
    public const string CONFIG_VERSION = "4";
    public const string NETWORK_NAME = "TYTYRI-6";
    public const int API_LEVEL = 6;
    public const int MIN_API_LEVEL = 6;

    public const int VIEW_INTERVAL = 60;
    public const byte STARTING_DIFFICULTY = 10;
    public const int MIN_PEERS = 4;
    public const int MAX_PEERS = 12;
    public const long DECIMAL_MULTIPLIER = 1_000_000;
    public const int VOTE_INTERVAL = 5;
    public const int EPOCH_LENGTH = 1440;
    public const int DIFFICULTY_LOOKBACK = 20;
    public const int EXPECTED_BLOCKS = 4;
    public const string MDNS_SERVICE_NAME = "_rpc._kryolite._tcp.local.";

    public static readonly ImmutableArray<Address> SEED_VALIDATORS =
    [
        "kryo:qpmfzymf76x7cdzx4ctrevan09ktgql3pz3txnx4txx284zs69enrnse"
    ];

    public static readonly Address DEV_FEE_ADDRESS = "kryo:qz02wegqcncudpcvjturahr3vf9vyaaacl05fm59kzc5f3hu9d053ard";

    public const long MIN_STAKE = 20_000 * DECIMAL_MULTIPLIER;
    public const ulong VALIDATOR_REWARD = 500 * DECIMAL_MULTIPLIER;
    public const long BLOCK_REWARD = 100 * DECIMAL_MULTIPLIER;
}
