using System.Collections.Immutable;

namespace Kryolite.Shared;

public static class Constant
{
    public const string STORE_VERSION = "4";
    public const string CONFIG_VERSION = "2";
    public const string NETWORK_NAME = "TYTYRI-3";
    public const int API_LEVEL = 5;
    public const int MIN_API_LEVEL = 5;

    public const int VIEW_INTERVAL = 60;
    public const byte STARTING_DIFFICULTY = 10;
    public const string ADDR_PREFIX = "kryo:";
    public const int MAX_PEERS = 12;
    public const long DECIMAL_MULTIPLIER = 1_000_000;
    public const int VOTE_INTERVAL = 5;
    public const int EPOCH_LENGTH = 1440;
    public const int DIFFICULTY_LOOKBACK = 20;
    public const int EXPECTED_BLOCKS = 4;

    public static readonly ImmutableArray<Address> SEED_VALIDATORS =
    [
        "kryo:adying4q6i54fywmy3fyu9gayfcvsajjq4hq67xx",
        "kryo:adqfedux7wjvtiazcu766vs77pcq9fdeequmkgxb"
    ];

    public static readonly Address DEV_FEE_ADDRESS = "kryo:aae9j3trpd4np32ew5it9hzgg3hq645kfxbd8azr";

    // Placeholder
    public const long MIN_STAKE = 20_000 * DECIMAL_MULTIPLIER;
    public const ulong VALIDATOR_REWARD = 1000 * DECIMAL_MULTIPLIER;
    public const long BLOCK_REWARD = 500 * DECIMAL_MULTIPLIER;
}
