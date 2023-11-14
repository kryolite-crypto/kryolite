using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using Avalonia.Data;
using Kryolite.Shared;

namespace Kryolite.Wallet;

public class SendTabViewModel : NotifyPropertyChanged
{
    public event EventHandler? SendTransactionClicked;

    private WalletModel? selectedWallet;
    private string? recipient;
    private string? amount;
    private ManifestView? manifest;
    private MethodView? method;
    private ObservableCollection<string> addresses = new();


    public WalletModel? SelectedWallet
    {
        get => selectedWallet; 
        set => RaisePropertyChanged(ref selectedWallet, value);
    }

    public string? Recipient
    {
        get => recipient; 
        set {
            var addr = new string((value ?? string.Empty).Where(c => Char.IsDigit(c) || Char.IsLetter(c) || c == ':').ToArray());

            RaisePropertyChanged(ref recipient, addr, () => {
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
    }

    public string? Amount
    {
        get => amount; 
        set => RaisePropertyChanged(ref amount, value, () => {
            if (value is null)
            {
                return "Amount is required";
            }

            if (!decimal.TryParse(value, out _))
            {
                return "Invalid value";
            }

            return null;
        });
    }

    public ManifestView? Manifest
    {
        get => manifest; 
        set => RaisePropertyChanged(ref manifest, value);
    }


    public MethodView? Method
    {
        get => method; 
        set => RaisePropertyChanged(ref method, value);
    }

    public ObservableCollection<string> Addresses
    {
        get => addresses; 
        set => RaisePropertyChanged(ref addresses, value);
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
