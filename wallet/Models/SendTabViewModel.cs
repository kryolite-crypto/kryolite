using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Data;
using Kryolite.Shared;

namespace Kryolite.Wallet;

public class SendTabViewModel : NotifyPropertyChanged
{
    public event EventHandler? SendTransactionClicked;

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

    private ManifestView? _Manifest;
    public ManifestView? Manifest
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

    private MethodView? _Method;
    public MethodView? Method
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

public class ManifestView
{
    public string Name { get; set; } = string.Empty;
    public List<MethodView> Methods { get; set; } = new();
}

public class MethodView
{
    public string Name { get; set; } = string.Empty;
    public List<ParamView> Params { get; set; } = new();
}

public class ParamView
{
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}
