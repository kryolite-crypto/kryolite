using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;

namespace Kryolite.Wallet;

public class WalletModel : NotifyPropertyChanged
{
    private string? _Description;
    public string? Description { 
        get => _Description;
        set {
            if (_Description != value)
            {
                _Description = value;
                RaisePropertyChanged();
            }
        }
    }

    public Address Address { get; set; } = new Address();

    public PublicKey PublicKey { get; set; } = new PublicKey();
    public PrivateKey PrivateKey { get; set; } = new PrivateKey();

    private long? _Balance;
    public long? Balance { 
        get => _Balance;
        set {
            if (_Balance != value)
            {
                _Balance = value;
                RaisePropertyChanged();
            }
        }
    }

    private long? _Pending;
    public long? Pending
    {
        get => _Pending;
        set
        {
            if (_Pending != value)
            {
                _Pending = value;
                RaisePropertyChanged();
            }
        }
    }

    public List<Transaction> WalletTransactions { get; set; } = new List<Transaction>();
}
