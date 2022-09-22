using System.Text;

namespace Marccacoin.Shared;

public static class Constant
{
    public const byte STARTING_DIFFICULTY = 20;
    public const int TARGET_BLOCK_TIME_S = 60;
    public const int EPOCH_LENGTH_BLOCKS = 100;
    public const double MINER_FEE = 0.75;
    public const double VALIDATOR_FEE = 0.2;
    public const double DEV_FEE = 0.05;
    public const string ADDR_PREFIX = "FIM0x";
    public const int MAX_MEMPOOL_TX = 100000;
    public const int MAX_BLOCK_TX = 20000;
}
