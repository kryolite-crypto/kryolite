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
    private ObservableCollection<AccountModel> accounts = new();

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

    public ObservableCollection<AccountModel> Accounts
    {
        get 
        {
            return accounts;
        }
        set
        {
            if (!Enumerable.SequenceEqual(accounts, value))
            {
                accounts = value;
                RaisePropertyChanged();
            }
        }
    }

    public void AddAccount(Account account)
    {
        Accounts.Add(new AccountModel
        {
            Address = account.Address,
            Description = account.Description,
            PublicKey = account.PublicKey,
            Balance = 0,
            Pending = 0
        });
    }

    public void UpdateWallet(Ledger ledger, List<TransactionModel> transactions)
    {
        var existing = Accounts
                .Where(x => x.Address == ledger.Address)
                .FirstOrDefault();

        if (existing == null)
        {
            return;
        }

        existing.Balance = ledger.Balance;
        existing.Pending = ledger.Pending;
        existing.Transactions = transactions;

        Balance = Accounts.Sum(x => (long)(x.Balance ?? 0));
        Pending = Accounts.Sum(x => (long)(x.Pending ?? 0));

        Transactions = Accounts
            .SelectMany(wallet => wallet.Transactions)
            .OrderByDescending(tx => tx.Timestamp)
            .Take(5)
            .ToList();
    }
}