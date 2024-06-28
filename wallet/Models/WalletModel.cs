using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using Kryolite.Type;

namespace Kryolite.Wallet;

public class AccountModel : NotifyPropertyChanged
{
    private string? description;
    private ulong? balance;
    private ulong? pending;

    public Address Address { get; set; } = new Address();
    public PublicKey PublicKey { get; set; } = new PublicKey();
    public List<TransactionModel> Transactions { get; set; } = new();

    public string? Description { 
        get => description;
        set => RaisePropertyChanged(ref description, value);
    }


    public ulong? Balance { 
        get => balance;
        set => RaisePropertyChanged(ref balance, value);
    }


    public ulong? Pending
    {
        get => pending;
        set => RaisePropertyChanged(ref pending, value);
    }
}
