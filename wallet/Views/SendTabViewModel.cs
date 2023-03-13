using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Data;
using Kryolite.Shared;

namespace Kryolite.Wallet;

public class SendTabViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? SendTransactionClicked;

    private void RaisePropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
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

    private ContractManifest? _Manifest;
    public ContractManifest? Manifest
    {
        get
        {
            return _Manifest;
        }
        set
        {
            if (_Manifest != value)
            {
                _Manifest = value;
                RaisePropertyChanged();
            }
        }
    }

    private ContractMethod? _Method;
    public ContractMethod? Method
    {
        get
        {
            return _Method;
        }
        set
        {
            if (_Method != value)
            {
                _Method = value;
                RaisePropertyChanged();
            }
        }
    }

    public void OnSendTransactionCommand()
    {
        SendTransactionClicked?.Invoke(this, null!);
    }
}
