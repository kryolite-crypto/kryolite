using System;

namespace Kryolite.Wallet;

public class AddressesTabViewModel : NotifyPropertyChanged
{
    public event EventHandler? NewAddressClicked;
    public event EventHandler? CopyAddressClicked;

    public StateModel State { get; } = StateModel.Instance;

    public void OnNewAddressCommand()
    {
        NewAddressClicked?.Invoke(this, null!);
    }

    public void OnCopyAddress()
    {
        CopyAddressClicked?.Invoke(this, null!);
    }
}
