using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Kryolite.Wallet;

public class AboutDialogViewModel : NotifyPropertyChanged
{
    private string _NetworkName = string.Empty;
    public string NetworkName
    {
        get
        {
            return _NetworkName;
        }
        set
        {
            if (_NetworkName != value)
            {
                _NetworkName = value;
                RaisePropertyChanged();
            }
        }
    }

    private string _Version = string.Empty;
    public string Version
    {
        get
        {
            return _Version;
        }
        set
        {
            if (_Version != value)
            {
                _Version = value;
                RaisePropertyChanged();
            }
        }
    }

    private bool _UpdateAvailable;
    public bool UpdateAvailable
    {
        get
        {
            return _UpdateAvailable;
        }
        set
        {
            if (_UpdateAvailable != value)
            {
                _UpdateAvailable = value;
                RaisePropertyChanged();
            }
        }
    }
}
