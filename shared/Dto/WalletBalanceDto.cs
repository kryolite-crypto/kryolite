namespace Kryolite.Shared.Dto;

public partial class WalletBalanceDto(Address address, ulong balance)
{
    public Address Address { get; } = address;
    public ulong Balance { get; } = balance;
}