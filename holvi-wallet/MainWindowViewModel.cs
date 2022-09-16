using System.ComponentModel;
using System.Runtime.CompilerServices;
using Marccacoin.Shared;
using System.Collections.Generic;

namespace holvi_wallet;

public class MainWindowViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private void RaisePropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private ulong _Balance;

    public ulong Balance
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

    private List<Transaction> _Transactions = new List<Transaction>();

    public List<Transaction> Transactions
    {
        get 
        {
            return _Transactions; 
        }
        set
        {
            if (_Transactions != value)
            {
                _Transactions = value;
                RaisePropertyChanged();
            }
        }
    }
}