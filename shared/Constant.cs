using System.Collections.Immutable;

namespace Kryolite.Shared;

public static class Constant
{
    public const string NETWORK_NAME = "KIRKNIEMI-4";
    public const int API_LEVEL = 3;
    public const int MIN_API_LEVEL = 3;

    public const int HEARTBEAT_INTERVAL = 60;
    public const byte STARTING_DIFFICULTY = 8;
    public const int TARGET_BLOCK_TIME_S = 60;
    public const int EPOCH_LENGTH_BLOCKS = 100;
    public const string ADDR_PREFIX = "kryo:";
    public const int MAX_MEMPOOL_TX = 100000;
    public const int MAX_BLOCK_TX = 20000;
    public const int MAX_PEERS = 6;
    public const long DECIMAL_MULTIPLIER = 1_000_000;

    public static readonly ImmutableArray<Address> SEED_VALIDATORS = ImmutableArray.Create<Address>(
        "kryo:wean6dt2ckvgubhh54ipu7nufdkfpmfx7zq9w2dx7e",
        "kryo:weamhh4gyqhr5vjuqk5jyx25giceyuuqq4cgrwit6i",
        "kryo:weacmn6cra2hif5an2858aqedjbz7vsp2r6h2pp3va"
    );

    // Placeholder
    public const long MIN_STAKE = 20_000 * DECIMAL_MULTIPLIER;
    public const long VALIDATOR_REWARD = 250 * DECIMAL_MULTIPLIER;
    public const long BLOCK_REWARD = 1000 * DECIMAL_MULTIPLIER;
    public const long DEV_REWARD = 50 * DECIMAL_MULTIPLIER;
}
