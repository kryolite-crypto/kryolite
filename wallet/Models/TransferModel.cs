using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using Kryolite.Shared;

namespace Kryolite.Wallet;

public class TransferModel : NotifyPropertyChanged
{
    private string? from;
    private string? to;
    private string amount = string.Empty;
    private List<AccountModel> wallets = new();
    private ulong min;
    private ulong max;
    private string recipientDescription = string.Empty;

    public string? From
    {
        get => from;
        set => RaisePropertyChanged(ref from, value);
    }

    public string? To
    {
        get => to;
        set => RaisePropertyChanged(ref to, value, () => {
            if (value is null)
            {
                return "Recipient is required";
            }
            
            if (!Address.IsValid(value))
            {
                return "Invalid address";
            }

            return null;
        });
    }

    public string Amount
    {
        get => amount;
        set => RaisePropertyChanged(ref amount, value, () => {
            if (value is null)
            {
                return "Amount required";
            }

            if (!decimal.TryParse(value, out var dec))
            {
                return "Invalid value";
            }
            
            if (dec < (min / Constant.DECIMAL_MULTIPLIER) || dec > (max / Constant.DECIMAL_MULTIPLIER))
            {
                return $"Amount must be between {min / Constant.DECIMAL_MULTIPLIER} and {max / Constant.DECIMAL_MULTIPLIER}";
            }

            return null;
        });
    }

    public List<AccountModel> Wallets
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
}
