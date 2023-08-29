using System.Collections.Immutable;
using System.Text;

namespace Kryolite.Shared;

public static class Constant
{
    public const int API_LEVEL = 1;
    public const int MIN_API_LEVEL = 1;

    public const int HEARTBEAT_INTERVAL = 60;
    public const byte STARTING_DIFFICULTY = 4;
    public const int TARGET_BLOCK_TIME_S = 60;
    public const int EPOCH_LENGTH_BLOCKS = 100;
    public const string ADDR_PREFIX = "kryo:";
    public const int MAX_MEMPOOL_TX = 100000;
    public const int MAX_BLOCK_TX = 20000;
    public const int MAX_PEERS = 6;
    public const long DECIMAL_MULTIPLIER = 1_000_000;

    public static readonly ImmutableArray<PublicKey> SEED_VALIDATORS = ImmutableArray.Create<PublicKey>(

    );

    // Placeholder
    public const long MIN_STAKE = 100_000 * DECIMAL_MULTIPLIER;
    public const long VALIDATOR_REWARD = 250 * DECIMAL_MULTIPLIER;
    public const long BLOCK_REWARD = 1000 * DECIMAL_MULTIPLIER;
    public const long DEV_REWARD = 50 * DECIMAL_MULTIPLIER;
}
