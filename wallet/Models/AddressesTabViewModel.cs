using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Kryolite.Wallet;

public class AddressesTabViewModel : NotifyPropertyChanged
{
    public event EventHandler? NewAddressClicked;
    public event EventHandler? CopyAddressClicked;

    public void OnNewAddressCommand()
    {
        NewAddressClicked?.Invoke(this, null!);
    }

    public void OnCopyAddress()
    {
        CopyAddressClicked?.Invoke(this, null!);
    }
}
