namespace Kryolite.Shared;

public enum TransactionType : byte
{
    PAYMENT,
    MINER_FEE,
    VALIDATOR_FEE,
    DEV_FEE,
    PROTX
}