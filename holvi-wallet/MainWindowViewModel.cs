using System.ComponentModel;
using System.Runtime.CompilerServices;
using Marccacoin.Shared;
using System.Collections.Generic;
using System.Linq;
using System;
using System.Collections.ObjectModel;
using Avalonia.Data;
using Avalonia;
using System.Threading.Tasks;

namespace holvi_wallet;

public class MainWindowViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? NewAddressClicked;
    public event EventHandler? SendTransactionClicked;
    public event EventHandler? CopyAddressClicked;
    public event EventHandler? ViewLogClicked;

    private void RaisePropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

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

    private WalletModel? _SelectedWallet;

    public WalletModel? SelectedWallet
    {
        get 
        {
            return _SelectedWallet; 
        }
        set
        {
            if (_SelectedWallet != value)
            {
                _SelectedWallet = value;
                RaisePropertyChanged();
            }
        }
    }

    private string? _Recipient;

    public string? Recipient
    {
        get 
        {
            return _Recipient; 
        }
        set
        {
            if (_Recipient != value)
            {
                if (!string.IsNullOrEmpty(value) && !Address.IsValid(value)) {
                    throw new DataValidationException("Invalid address");
                }

                _Recipient = value;
                RaisePropertyChanged();
            }
        }
    }

    private string? _Amount;

    public string? Amount
    {
        get 
        {
            return _Amount;
        }
        set
        {
            if (_Amount != value)
            {
                if (!string.IsNullOrEmpty(value) && !decimal.TryParse(value, out _)) {
                    throw new DataValidationException("Invalid decimal value");
                }

                _Amount = value;
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
                Balance = _Wallets.Sum(x => (long)(x.Balance ?? 0));
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
            _Wallets.Insert(_Wallets.Count, new WalletModel {
                Description = wallet.Description,
                Address = wallet.Address,
                PublicKey = wallet.PublicKey,
                PrivateKey = wallet.PrivateKey,
                Balance = wallet.Balance,
                WalletTransactions = wallet.WalletTransactions
            });
        } else {
            Wallets[Wallets.IndexOf(existing)].Balance = wallet.Balance;
            Wallets[Wallets.IndexOf(existing)].WalletTransactions = wallet.WalletTransactions;
        }

        Balance = Wallets.Sum(x => (long)(x.Balance ?? 0));

        Transactions = Wallets.SelectMany(wallet => wallet.WalletTransactions)
            .OrderByDescending(tx => tx.Timestamp)
            .Take(5)
            .ToList();
    }

    public void OnNewAddressCommand()
    {
        NewAddressClicked?.Invoke(this, null!);
    }

    public void OnSendTransactionCommand()
    {
        SendTransactionClicked?.Invoke(this, null!);
    }

    public void OnCopyAddress()
    {
        CopyAddressClicked?.Invoke(this, null!);
    }

    public void ViewLogCommand()
    {
        ViewLogClicked?.Invoke(this, null!);
    }
}
