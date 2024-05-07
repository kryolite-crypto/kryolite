using System.Collections.Immutable;

namespace Kryolite.Shared;

public static class Constant
{
    public const string STORE_VERSION = "5";
    public const string CONFIG_VERSION = "3";
    public const string NETWORK_NAME = "TYTYRI-6";
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
        "kryo:adcun6jzg5df27gvueh8sc5mctt3wht8qidhx3xd",
        "kryo:aafj8hfvxa9fsrbpzh9sux3jvqh4zd3skbedzhsc",
        "kryo:abgb3hiv63mwsitjizvh53ph5drab5xcu28ngzxb" // TODO: remove
    ];

    public static readonly Address DEV_FEE_ADDRESS = "kryo:aae9j3trpd4np32ew5it9hzgg3hq645kfxbd8azr";

    public const long MIN_STAKE = 20_000 * DECIMAL_MULTIPLIER;
    public const ulong VALIDATOR_REWARD = 500 * DECIMAL_MULTIPLIER;
    public const long BLOCK_REWARD = 100 * DECIMAL_MULTIPLIER;
}
