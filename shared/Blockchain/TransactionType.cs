namespace Kryolite.Shared;

public enum TransactionType : byte
{
    PAYMENT,
    BLOCK_REWARD,
    STAKE_REWARD,
    CONTRACT,
    REG_VALIDATOR,
    DEV_REWARD
}
