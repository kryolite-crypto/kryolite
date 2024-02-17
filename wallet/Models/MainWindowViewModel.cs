using System.Linq;
using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using Kryolite.Shared.Blockchain;
using Kryolite.Shared;
using System.Runtime.InteropServices;

namespace Kryolite.Wallet;

public class MainWindowViewModel : NotifyPropertyChanged
{
    public event EventHandler? ViewLogClicked;
    public event EventHandler? AboutClicked;
    
    private long blocks;
    private int connectedPeers;

    public StateModel State { get; } = StateModel.Instance;

    public bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    public long Blocks
    {
        get => blocks;
        set => RaisePropertyChanged(ref blocks, value);
    }


    public int ConnectedPeers
    {
        get => connectedPeers;
        set => RaisePropertyChanged(ref connectedPeers, value);
    }

    public void ViewLogCommand()
    {
        ViewLogClicked?.Invoke(this, null!);
    }

    public void AboutCommand()
    {
        AboutClicked?.Invoke(this, null!);
    }
}
