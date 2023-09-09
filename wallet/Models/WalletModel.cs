using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;

namespace Kryolite.Wallet;

public class WalletModel : NotifyPropertyChanged
{
    private string? description;
    private long? balance;
    private long? pending;

    public Address Address { get; set; } = new Address();
    public PublicKey PublicKey { get; set; } = new PublicKey();
    public PrivateKey PrivateKey { get; set; } = new PrivateKey();
    public List<TransactionModel> Transactions { get; set; } = new();

    public string? Description { 
        get => description;
        set => RaisePropertyChanged(ref description, value);
    }


    public long? Balance { 
        get => balance;
        set => RaisePropertyChanged(ref balance, value);
    }


    public long? Pending
    {
        get => pending;
        set => RaisePropertyChanged(ref pending, value);
    }
}
