namespace Kryolite.Shared;

public enum TransactionType : byte
{
    GENESIS,
    PAYMENT,
    BLOCK,
    VIEW,
    CONTRACT,
    REGISTER_VALIDATOR
}
