namespace Kryolite.Shared;

public enum TransactionType : byte
{
    PAYMENT,
    GENESIS,
    BLOCK,
    VIEW,
    CONTRACT,
    REG_VALIDATOR,
    VOTE
}
