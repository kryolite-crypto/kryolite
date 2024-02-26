using System;
using System.Collections.ObjectModel;
using System.Linq;
using Kryolite.Shared;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.Painting.Effects;
using LiveChartsCore.SkiaSharpView.VisualElements;
using SkiaSharp;

namespace Kryolite.Wallet;

public class MiningTabModel : NotifyPropertyChanged
{
    private AccountModel? _selectedWallet;
    private string? _threads;
    private string _actionText = "Start mining";
    private string _hashrate = "0 h/s";
    private string _currentDifficulty = "N/A";
    private long _blocksFound;
    private ulong _blockReward;

    public StateModel State { get; } = StateModel.Instance;

    public AccountModel? SelectedWallet
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

    public ObservableCollection<ObservableValue> ChartValues {  get; set; } = new ObservableCollection<ObservableValue>();

    public ObservableCollection<ISeries> Series { get; set; } = new ObservableCollection<ISeries>();

    public Axis[] XAxes { get; set; }
        = new Axis[]
        {
            new Axis
            {
                Name = null,
                LabelsPaint = new SolidColorPaint(SKColors.Empty), 
                SeparatorsPaint = new SolidColorPaint(SKColors.Empty)
                { 
                    StrokeThickness = 1
                }
            }
        };

    public Axis[] YAxes { get; set; }
        = new Axis[]
        {
            new Axis
            {
                Name = null,
                LabelsPaint = new SolidColorPaint(SKColors.Empty), 
                SeparatorsPaint = new SolidColorPaint(SKColors.Empty)
                { 
                    StrokeThickness = 1
                } 
            }
        };
}
