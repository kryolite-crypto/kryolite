using Kryolite.Shared;

namespace Kryolite.Wallet;

public class TransactionModel : NotifyPropertyChanged
{
    private Address _Recipient = Address.NULL_ADDRESS;
    public Address Recipient { 
        get => _Recipient;
        set {
            if (_Recipient != value)
            {
                _Recipient = value;
                RaisePropertyChanged();
            }
        }
    }

    private long _Amount { get; set; }
    public long Amount { 
        get => _Amount;
        set {
            if (_Amount != value)
            {
                _Amount = value;
                RaisePropertyChanged();
            }
        }
    }

    private long _Timestamp { get; set; }
    public long Timestamp { 
        get => _Timestamp;
        set {
            if (_Timestamp != value)
            {
                _Timestamp = value;
                RaisePropertyChanged();
            }
        }
    }

}