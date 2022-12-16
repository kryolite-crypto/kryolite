using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Kryolite.Shared;

namespace Kryolite.Wallet;

public class WalletModel : INotifyPropertyChanged
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

    public string? Address { get; set; }

    public PublicKey PublicKey { get; set; }
    
    public PrivateKey PrivateKey { get; set; }
    private ulong? _Balance;
    public ulong? Balance { 
        get => _Balance;
        set {
            if (_Balance != value)
            {
                _Balance = value;
                RaisePropertyChanged();
            }
        }
    }

    public Kryolite.Shared.Wallet Wallet { get; set; }

    public List<WalletTransaction> WalletTransactions { get; set; } = new List<WalletTransaction>();

    public event PropertyChangedEventHandler? PropertyChanged;

    private void RaisePropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
