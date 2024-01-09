using System;
using System.Collections.ObjectModel;
using System.Linq;
using Kryolite.Shared;

namespace Kryolite.Wallet;

public class MiningTabModel : NotifyPropertyChanged
{
    private WalletModel? _selectedWallet;
    private string? _threads;
    private string _actionText = "Start mining";
    private string _hashrate = "0 h/s";
    private string _currentDifficulty = "N/A";
    private long _blocksFound;
    private ulong _blockReward;

    public WalletModel? SelectedWallet
    {
        get => _selectedWallet; 
        set => RaisePropertyChanged(ref _selectedWallet, value);
    }

    public string? Threads
    {
        get => _threads; 
        set => RaisePropertyChanged(ref _threads, value);
    }

    public string ActionText
    {
        get => _actionText; 
        set => RaisePropertyChanged(ref _actionText, value);
    }

    public string Hashrate
    {
        get => _hashrate; 
        set => RaisePropertyChanged(ref _hashrate, value);
    }

    public string CurrentDifficulty
    {
        get => _currentDifficulty; 
        set => RaisePropertyChanged(ref _currentDifficulty, value);
    }

    public long BlocksFound
    {
        get => _blocksFound; 
        set => RaisePropertyChanged(ref _blocksFound, value);
    }

    public ulong BlockReward
    {
        get => _blockReward; 
        set => RaisePropertyChanged(ref _blockReward, value);
    }
}
