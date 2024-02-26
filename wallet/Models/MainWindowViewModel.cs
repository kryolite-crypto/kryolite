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
    
    private bool firstTimeExperience;
    private bool welcomePage;
    private bool newSeedPage;
    private bool importSeedPage;
    private long blocks;
    private int connectedPeers;
    private string mnemonic = string.Empty;
    private string error = string.Empty;

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

    public bool FirstTimeExperience
    {
        get => firstTimeExperience;
        set => RaisePropertyChanged(ref firstTimeExperience, value);
    }

    public bool WelcomePage
    {
        get => welcomePage;
        set => RaisePropertyChanged(ref welcomePage, value);
    }

    public bool NewSeedPage
    {
        get => newSeedPage;
        set => RaisePropertyChanged(ref newSeedPage, value);
    }

    public bool ImportSeedPage
    {
        get => importSeedPage;
        set => RaisePropertyChanged(ref importSeedPage, value);
    }

    public string Mnemonic
    {
        get => mnemonic;
        set => RaisePropertyChanged(ref mnemonic, value);
    }

    public string Error
    {
        get => error;
        set => RaisePropertyChanged(ref error, value);
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
