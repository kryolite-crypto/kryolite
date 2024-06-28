using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Kryolite.Shared;
using Kryolite.Type;

namespace Kryolite.Wallet;

public class ValidatorViewModel : NotifyPropertyChanged
{
    private Address address = Address.NULL_ADDRESS;
    private Address? rewardAddress;
    private string status = "Disabled";
    private string balance = $"0 / {Constant.MIN_STAKE}";
    private ulong accumulatedReward;
    private DateTimeOffset nextEpoch;
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

    public string Balance {
        get => balance;
        set => RaisePropertyChanged(ref balance, value);
    }

    public ulong AccumulatedReward {
        get => accumulatedReward;
        set => RaisePropertyChanged(ref accumulatedReward, value);
    }

    public DateTimeOffset NextEpoch {
        get => nextEpoch;
        set => RaisePropertyChanged(ref nextEpoch, value);
    }

    public string Status {
        get => status;
        set => RaisePropertyChanged(ref status, value);
    }

    public List<TransactionModel> Votes {
        get => votes;
        set => RaisePropertyChanged(ref votes, value);
    }

    public string ActionText {
        get => actionText;
        set => RaisePropertyChanged(ref actionText, value);
    }

    public void SetBalance(ulong balance)
    {
        if (balance >= Constant.MIN_STAKE)
        {
            Balance = $"{balance / (decimal)Constant.DECIMAL_MULTIPLIER} KRYO";
            return;
        }

        Balance = $"{balance / (decimal)Constant.DECIMAL_MULTIPLIER} KRYO / {Constant.MIN_STAKE / (decimal)Constant.DECIMAL_MULTIPLIER} KRYO";
    }
}
