using System.Linq;
using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using Kryolite.Shared.Blockchain;
using Kryolite.Shared;

namespace Kryolite.Wallet;

public class MainWindowViewModel : NotifyPropertyChanged
{
    public event EventHandler? ViewLogClicked;
    public event EventHandler? AboutClicked;

    private long _Blocks;

    public long Blocks
    {
       get 
        {
            return _Blocks; 
        }
        set
        {
            if (_Blocks != value)
            {
                _Blocks = value;
                RaisePropertyChanged();
            }
        }
    }

    private int _ConnectedPeers;
    public int ConnectedPeers
    {
       get 
        {
            return _ConnectedPeers; 
        }
        set
        {
            if (_ConnectedPeers != value)
            {
                _ConnectedPeers = value;
                RaisePropertyChanged();
            }
        }
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

    private long _Pending;
    public long Pending
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

    private List<Transaction> _Transactions = new List<Transaction>();
    public List<Transaction> Transactions
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

    private ObservableCollection<WalletModel> _Wallets = new ObservableCollection<WalletModel>();

    public ObservableCollection<WalletModel> Wallets
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
                RaisePropertyChanged();
            }
        }
    }

    public void AddWallet(Shared.Wallet wallet)
    {
        Wallets.Add(new WalletModel
        {
            Address = wallet.Address,
            Description = wallet.Description,
            PublicKey = wallet.PublicKey,
            PrivateKey = wallet.PrivateKey,
            Balance = 0,
            Pending = 0
        });
    }

    public void UpdateWallet(Ledger ledger, List<Transaction> transactions)
    {
        var existing = Wallets
                .Where(x => x.Address == ledger.Address)
                .FirstOrDefault();

        if (existing == null)
        {
            return;
        }

        existing.Balance = ledger.Balance;
        existing.Pending = ledger.Pending;
        existing.WalletTransactions = transactions;

        Balance = Wallets.Sum(x => (long)(x.Balance ?? 0));
        Pending = Wallets.Sum(x => (long)(x.Pending ?? 0));

        Transactions = Wallets.SelectMany(wallet => wallet.WalletTransactions)
            .OrderByDescending(tx => tx.Timestamp)
            .Take(5)
            .ToList();
    }

    public void ViewLogCommand()
    {
        ViewLogClicked?.Invoke(this, null!);
    }

    public void AboutCommand()
    {
        AboutClicked?.Invoke(this, null!);
    }
}
