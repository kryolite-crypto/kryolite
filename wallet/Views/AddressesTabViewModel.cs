using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Kryolite.Wallet;

public class AddressesTabViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? NewAddressClicked;
    public event EventHandler? CopyAddressClicked;

    private void RaisePropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public void OnNewAddressCommand()
    {
        NewAddressClicked?.Invoke(this, null!);
    }

    public void OnCopyAddress()
    {
        CopyAddressClicked?.Invoke(this, null!);
    }
}
