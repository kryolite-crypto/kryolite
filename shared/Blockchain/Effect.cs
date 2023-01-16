namespace Kryolite.Shared;

public class Effect
{
    public Guid Id { get; set; }
    public Guid TransactionId { get; set; }
    public Address To { get; set; }
    public ulong Value { get; set; }
    
    public Effect(Address to, ulong value)
    {
        To = to ?? throw new ArgumentNullException(nameof(to));
        Value = value;
    }
}
