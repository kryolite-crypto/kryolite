using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Kryolite.Shared;

namespace Kryolite.Wallet;

public class StateModel : NotifyPropertyChanged
{
    public static readonly StateModel Instance = new();

    private long balance;
    private long pending;
    private List<TransactionModel> transactions = new();
    private ObservableCollection<WalletModel> wallets = new();

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

    private StateModel()
    {

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

        Transactions = Wallets
            .SelectMany(wallet => wallet.Transactions)
            .OrderByDescending(tx => tx.Timestamp)
            .Take(5)
            .ToList();
    }
}