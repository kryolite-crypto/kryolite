using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Kryolite.Wallet;

public class AboutDialogViewModel : NotifyPropertyChanged
{
    private string networkName = string.Empty;
    private string version = string.Empty;
    private bool updateAvailable;

    public string NetworkName
    {
        get => networkName;
        set => RaisePropertyChanged(ref networkName, value);
    }


    public string Version
    {
        get => version;
        set => RaisePropertyChanged(ref version, value);
    }


    public bool UpdateAvailable
    {
        get => updateAvailable;
        set => RaisePropertyChanged(ref updateAvailable, value);
    }
}
