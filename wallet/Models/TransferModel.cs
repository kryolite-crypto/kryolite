using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using Kryolite.Shared;

namespace Kryolite.Wallet;

public class TransferModel : NotifyPropertyChanged, INotifyDataErrorInfo
{
    private string? from;
    private string? to;
    private string amount = string.Empty;
    private List<WalletModel> wallets = new();
    private ulong min;
    private ulong max;
    private string recipientDescription = string.Empty;

    public string? From
    {
        get => from;
        set => RaisePropertyChanged(ref from, value);
    }

    private string? _addressError = null;
    public string? To
    {
        get => to;
        set {
            RaisePropertyChanged(ref to, value);

            if (to is null || !Address.IsValid(to))
            {
                _addressError = "Invalid address";
            }
            else
            {
                _addressError = null;
            }

            ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(nameof(To)));
        }
    }

    private string? _amountError = null;
    public string Amount
    {
        get => amount;
        set {
            RaisePropertyChanged(ref amount, value);

            if (!decimal.TryParse(value, out var dec) || dec < (min / Constant.DECIMAL_MULTIPLIER) || dec > (max / Constant.DECIMAL_MULTIPLIER))
            {
                _amountError = $"Amount must be between {min / Constant.DECIMAL_MULTIPLIER} and {max / Constant.DECIMAL_MULTIPLIER}";
            }
            else
            {
                _amountError = null;
            }

            ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(nameof(Amount)));
        }
    }

    public List<WalletModel> Wallets
    {
        get => wallets;
        set => RaisePropertyChanged(ref wallets, value);
    }

    public ulong Min
    {
        get => min;
        set => RaisePropertyChanged(ref min, value);
    }

    public ulong Max
    {
        get => max;
        set => RaisePropertyChanged(ref max, value);
    }

    public string RecipientDescription
    {
        get => recipientDescription;
        set => RaisePropertyChanged(ref recipientDescription, value);
    }

    public bool HasErrors => false;

    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;

    public IEnumerable GetErrors(string? propertyName) =>
        propertyName switch
        {
            nameof(To) => new[] { _addressError },
            nameof(Amount) => new[] { _amountError },
            _ => new string[0]
        };
}
