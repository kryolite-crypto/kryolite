namespace Kryolite.Shared;

public enum TransactionType : byte
{
    PAYMENT,
    BLOCK_REWARD,
    STAKE_REWARD,
    CONTRACT,
    REGISTER_VALIDATOR,
    DEV_REWARD,
    DEREGISTER_VALIDATOR
}
