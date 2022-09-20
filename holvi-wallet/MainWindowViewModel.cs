using System.ComponentModel;
using System.Runtime.CompilerServices;
using Marccacoin.Shared;
using System.Collections.Generic;
using System.Linq;
using System;
using System.Collections.ObjectModel;

namespace holvi_wallet;

public class MainWindowViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? NewAddressClicked;

    private void RaisePropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private long _Balance;

    public long Balance
    {
        get 
        {
            return _Balance; 
        }
        set
        {
            if (_Balance != value)
            {
                _Balance = value;
                RaisePropertyChanged();
            }
        }
    }

    private ulong _Pending;

    public ulong Pending
    {
        get 
        {
            return _Pending; 
        }
        set
        {
            if (_Pending != value)
            {
                _Pending = value;
                RaisePropertyChanged();
            }
        }
    }

    private List<WalletTransaction> _Transactions = new List<WalletTransaction>();

    public List<WalletTransaction> Transactions
    {
        get 
        {
            return _Transactions; 
        }
        set
        {
            if (!Enumerable.SequenceEqual(_Transactions, value))
            {
                _Transactions = value;
                RaisePropertyChanged();
            }
        }
    }

    private ObservableCollection<Wallet> _Wallets = new ObservableCollection<Wallet>();

    public ObservableCollection<Wallet> Wallets
    {
        get 
        {
            return _Wallets;
        }
        set
        {
            if (!Enumerable.SequenceEqual(_Wallets, value))
            {
                _Wallets = value;
                Balance = _Wallets.Sum(x => (long)x.Balance);
                Transactions = _Wallets.SelectMany(wallet => wallet.WalletTransactions)
                    .OrderByDescending(tx => tx.Timestamp)
                    .Take(5)
                    .ToList();

                RaisePropertyChanged();
            }
        }
    }

    public void SetWallet(Wallet wallet)
    {
        var existing = Wallets
                .Where(x => x.Address == wallet.Address)
                .FirstOrDefault();

        if (existing == null) {
            _Wallets.Insert(_Wallets.Count, wallet);
        } else {
            Wallets[Wallets.IndexOf(existing)] = wallet;
        }

        Balance = Wallets.Sum(x => (long)x.Balance);
        Transactions = Wallets.SelectMany(wallet => wallet.WalletTransactions)
            .OrderByDescending(tx => tx.Timestamp)
            .Take(5)
            .ToList();
    }

    public void OnNewAddressCommand()
    {
        NewAddressClicked?.Invoke(this, null!);
    }
}
