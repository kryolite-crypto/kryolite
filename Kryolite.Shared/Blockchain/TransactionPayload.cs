namespace Kryolite.Shared;

public class TransactionPayload : ISerializable
{
    public ITransactionPayload? Payload;

    public byte GetSerializerId()
    {
        return (byte)SerializerEnum.TRANSACTION_PAYLOAD;
    }

    public int GetLength() =>
        Serializer.SizeOfN(Payload);

    public TransactionPayload Create<TransactionPayload>() where TransactionPayload : new()
    {
        return new TransactionPayload();
    }

    public void Serialize(ref Serializer serializer)
    {
        switch (Payload)
        {
            case CallMethod:
                serializer.Write(Payload.GetSerializerId());
                serializer.Write(Payload);
            break;
            case NewContract:
                serializer.Write(Payload.GetSerializerId());
                serializer.Write(Payload);
            break;
            default:
                serializer.Write((byte)0);
            break;
        }
    }

    public void Deserialize(ref Serializer serializer)
    {
        byte type = 0;
        serializer.Read(ref type);

        switch ((SerializerEnum)type)
        {
            case SerializerEnum.CALL_METHOD:
                var callMethod = new CallMethod();
                serializer.Read(ref callMethod);
                Payload = callMethod;
            break;
            case SerializerEnum.NEW_CONTRACT:
                var newContract = new NewContract();
                serializer.Read(ref newContract);
                Payload = newContract;
            break;
        }
    }
}

public interface ITransactionPayload : ISerializable
{

}
