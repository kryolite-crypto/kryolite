namespace Kryolite.Shared;

public class LedgerWallet
{
    public Guid Id { get; set; }
    public Address Address { get; set; }
    public ulong Balance { get; set; }
    public ulong Pending { get; set; }

    public LedgerWallet(Address address) => Address = address;
}
