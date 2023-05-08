using System.Text;

namespace Kryolite.Shared;

public static class Constant
{
    public static Version MIN_SUPPORTED_VERSION { get; } = new Version(1, 1, 0);

    public const byte STARTING_DIFFICULTY = 8;
    public const int TARGET_BLOCK_TIME_S = 60;
    public const int EPOCH_LENGTH_BLOCKS = 100;
    public const string ADDR_PREFIX = "kryo:";
    public const int MAX_MEMPOOL_TX = 100000;
    public const int MAX_BLOCK_TX = 20000;
    public const int MAX_PEERS = 6;

    // Placeholder
    public const long POS_REWARD = 250_000_000;
    public const long POW_REWARD = 1000_000_000;
    public const long DEV_REWARD = 50_000_000;
}
