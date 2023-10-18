using System.Linq;
using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using Kryolite.Shared.Blockchain;
using Kryolite.Shared;
using System.Runtime.InteropServices;

namespace Kryolite.Wallet;

public class MainWindowViewModel : NotifyPropertyChanged
{
    public event EventHandler? ViewLogClicked;
    public event EventHandler? AboutClicked;
    
    private long blocks;
    private int connectedPeers;
    private long balance;
    private long pending;
    private List<TransactionModel> transactions = new List<TransactionModel>();
    private ObservableCollection<WalletModel> wallets = new ObservableCollection<WalletModel>();

    public bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    public long Blocks
    {
        get => blocks;
        set => RaisePropertyChanged(ref blocks, value);
    }


    public int ConnectedPeers
    {
        get => connectedPeers;
        set => RaisePropertyChanged(ref connectedPeers, value);
    }


    public long Balance
    {
        get => balance;
        set => RaisePropertyChanged(ref balance, value);
    }


    public long Pending
    {
        get => pending;
        set => RaisePropertyChanged(ref pending, value);
    }


    public List<TransactionModel> Transactions
    {
        get 
        {
            return transactions; 
        }
        set
        {
            if (!Enumerable.SequenceEqual(transactions, value))
            {
                transactions = value;
                RaisePropertyChanged();
            }
        }
    }



    public ObservableCollection<WalletModel> Wallets
    {
        get 
        {
            return wallets;
        }
        set
        {
            if (!Enumerable.SequenceEqual(wallets, value))
            {
                wallets = value;
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

    public void UpdateWallet(Ledger ledger, List<TransactionModel> transactions)
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
        existing.Transactions = transactions;

        Balance = Wallets.Sum(x => (long)(x.Balance ?? 0));
        Pending = Wallets.Sum(x => (long)(x.Pending ?? 0));

        Transactions = Wallets.SelectMany(wallet => wallet.Transactions)
            .OrderByDescending(tx => tx.Timestamp)
            .DistinctBy(tx => $"{tx.Recipient},{tx.Timestamp},{tx.Amount}")
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
