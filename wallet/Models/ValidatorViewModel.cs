using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Kryolite.Shared;

namespace Kryolite.Wallet;

public class ValidatorViewModel : NotifyPropertyChanged
{
    private Address address = Address.NULL_ADDRESS;
    private Address? rewardAddress;
    private string status = "Disabled";
    private ulong total;
    private ulong locked;
    private ulong available;
    private List<TransactionModel> votes = new();
    private string actionText = "Enable Validator";


    public Address Address { 
        get => address;
        set => RaisePropertyChanged(ref address, value);
    }

    public Address? RewardAddress { 
        get => rewardAddress;
        set => RaisePropertyChanged(ref rewardAddress, value);
    }

    public string Status {
        get => status;
        set => RaisePropertyChanged(ref status, value);
    }

    public ulong Total { 
        get => total;
        set => RaisePropertyChanged(ref total, value);
    }

    public ulong Locked { 
        get => locked;
        set => RaisePropertyChanged(ref locked, value);
    }

    public ulong Available { 
        get => available;
        set => RaisePropertyChanged(ref available, value);
    }

    public List<TransactionModel> Votes {
        get => votes;
        set => RaisePropertyChanged(ref votes, value);
    }

    public string ActionText {
        get => actionText;
        set => RaisePropertyChanged(ref actionText, value);
    }
}
