namespace Kryolite.Shared;

public enum TransactionType : byte
{
    GENESIS,
    PAYMENT,
    BLOCK,
    HEARTBEAT,
    CONTRACT,
    REGISTER_VALIDATOR
}
