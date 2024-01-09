using System.Collections.Immutable;

namespace Kryolite.Shared;

public static class Constant
{
    public const string STORE_VERSION = "3";
    public const string NETWORK_NAME = "TYTYRI-2";
    public const int API_LEVEL = 4;
    public const int MIN_API_LEVEL = 3;

    public const int HEARTBEAT_INTERVAL = 60;
    public const byte STARTING_DIFFICULTY = 10;
    public const string ADDR_PREFIX = "kryo:";
    public const int MAX_PEERS = 6;
    public const long DECIMAL_MULTIPLIER = 1_000_000;
    public const int VOTE_INTERVAL = 5;
    public const int EPOCH_LENGTH = 1440;

    public static readonly ImmutableArray<Address> SEED_VALIDATORS =
    [
        "kryo:wean6dt2ckvgubhh54ipu7nufdkfpmfx7zq9w2dx7e",
        "kryo:weamhh4gyqhr5vjuqk5jyx25giceyuuqq4cgrwit6i",
        "kryo:weacmn6cra2hif5an2858aqedjbz7vsp2r6h2pp3va",
        "kryo:weaqr9zjggwru75qfgwkw5nhygtwuxv89hrhgnigve"
    ];

    public static readonly Address DEV_FEE_ADDRESS = "kryo:weabq9evqg43d4q9e9rbcjjq93j9xauu7g2hxwyjne";

    // Placeholder
    public const long MIN_STAKE = 20_000 * DECIMAL_MULTIPLIER;
    public const ulong VALIDATOR_REWARD = 1000 * DECIMAL_MULTIPLIER;
    public const long BLOCK_REWARD = 1000 * DECIMAL_MULTIPLIER;
}
