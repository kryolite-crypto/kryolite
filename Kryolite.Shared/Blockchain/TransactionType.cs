using Kryolite.ByteSerializer;

namespace Kryolite.Shared;

public enum TransactionType : byte
{
    PAYMENT,
    BLOCK_REWARD,
    STAKE_REWARD,
    CONTRACT,
    REGISTER_VALIDATOR,
    DEV_REWARD,
    DEREGISTER_VALIDATOR,
    CONTRACT_SCHEDULED_SELF_CALL
}

public static class TransactionTypeSerializer
{
    public static void Write(this ref Serializer serializer, TransactionType value)
    {
        serializer.Write((byte)value);
    }

    public static void Read(this ref Serializer serializer, ref TransactionType value)
    {
        byte b = 0;
        serializer.Read(ref b);
        value = (TransactionType)b;
    }
}
