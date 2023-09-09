using Kryolite.Shared;

namespace Kryolite.Wallet;

public class TransactionModel : NotifyPropertyChanged
{
    private Address recipient = Address.NULL_ADDRESS;
    private long amount;
    private long timestamp;


    public Address Recipient { 
        get => recipient;
        set => RaisePropertyChanged(ref recipient, value);
    }

    public long Amount { 
        get => amount;
        set => RaisePropertyChanged(ref amount, value);
    }

    public long Timestamp { 
        get => timestamp;
        set => RaisePropertyChanged(ref timestamp, value);
    }
}
