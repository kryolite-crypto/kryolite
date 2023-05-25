using Avalonia.Controls;
using System;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks;
using Avalonia.Threading;
using System.Threading.Tasks.Dataflow;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
using System.Collections.Generic;
using System.Reactive.Linq;
using Avalonia.Markup.Xaml;
using Kryolite.Node;

namespace Kryolite.Wallet;

public partial class MainWindow : Window
{
    private IBlockchainManager BlockchainManager;
    private INetworkManager NetworkManager;
    private IWalletManager WalletManager;
    private IMeshNetwork MeshNetwork;

    private MainWindowViewModel Model = new MainWindowViewModel();

    public MainWindow()
    {
        BlockchainManager = Program.ServiceCollection.GetService<IBlockchainManager>() ?? throw new ArgumentNullException(nameof(IBlockchainManager));
        NetworkManager = Program.ServiceCollection.GetService<INetworkManager>() ?? throw new ArgumentNullException(nameof(INetworkManager));
        WalletManager = Program.ServiceCollection.GetService<IWalletManager>() ?? throw new ArgumentNullException(nameof(IWalletManager));
        MeshNetwork = Program.ServiceCollection.GetService<IMeshNetwork>() ?? throw new ArgumentNullException(nameof(IMeshNetwork));

        AvaloniaXamlLoader.Load(this);

#if DEBUG
        this.AttachDevTools();
#endif

        DataContext = Model;

        this.Opened += OnInitialized;

        Model.ViewLogClicked += (object? sender, EventArgs args) => {
            var dialog = new LogViewerDialog();
            dialog.Show(this);
        };

        Model.AboutClicked += (object? sender, EventArgs args) => {
            var dialog = new AboutDialog();
            dialog.Show(this);
        };

        Node.MeshNetwork.ConnectedChanged += async (object? sender, int count) => {
            await Dispatcher.UIThread.InvokeAsync(() => {
                Model.ConnectedPeers = count;
            });
        };

        ChainObserver.BeginSync += async (object? sender, long total) => {
            var syncProgress = this.FindControl<ProgressBar>("SyncProgress");

            if (syncProgress is null)
            {
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(() => {
                syncProgress.Value = 0;
                syncProgress.Minimum = 0;
                syncProgress.Maximum = 100;
                syncProgress.IsEnabled = true;
                syncProgress.IsVisible = true;
            });
        };

        var progressUpdatedBuffer = new BufferBlock<SyncEventArgs>();

        progressUpdatedBuffer.AsObservable()
            .Buffer(TimeSpan.FromSeconds(1), 250)
            .Subscribe(async syncArgs => await OnProgressUpdated(syncArgs));

        ChainObserver.SyncProgress += (object? sender, SyncEventArgs e) => {
            progressUpdatedBuffer.Post(e);
        };

        ChainObserver.EndSync += async (object? sender, EventArgs args) => {
            var syncProgress = this.FindControl<ProgressBar>("SyncProgress");

            if (syncProgress is null)
            {
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(() => {
                syncProgress.Value = 0;
                syncProgress.IsEnabled = false;
                syncProgress.IsVisible = false;
            });
        };

        BlockchainManager.OnChainUpdated(new ActionBlock<ChainState>(async state => {
            await Dispatcher.UIThread.InvokeAsync(() => {
                Model.Blocks = state.Height;
            });
        }));

        BlockchainManager.OnWalletUpdated(new ActionBlock<Kryolite.Shared.Wallet>(async wallet => await OnWalletUpdated(wallet)));
    }

    private void OnInitialized(object? sender, EventArgs args)
    {
        Task.Run(async () => {
            var wallets = WalletManager.GetWallets().Values
                .Where(wallet => wallet.WalletType == Shared.WalletType.WALLET)
                .Select(wallet => new WalletModel {
                    Description = wallet.Description,
                    Address = wallet.Address,
                    PublicKey = wallet.PublicKey,
                    PrivateKey = wallet.PrivateKey,
                    Balance = wallet.Balance,
                    WalletTransactions = wallet.WalletTransactions,
                    Wallet = wallet
                });

            var balance = wallets.Sum(x => (long)(x.Balance ?? 0));
            var transactions = wallets.SelectMany(wallet => wallet.WalletTransactions)
                .OrderByDescending(tx => tx.Timestamp)
                .Take(5)
                .ToList();

            await Dispatcher.UIThread.InvokeAsync(() => {
                Model.Wallets = new ObservableCollection<WalletModel>(wallets);
                Model.Balance = balance;
                Model.Transactions = transactions;
            });                    
        });

        Task.Run(async () => {
            var state = BlockchainManager.GetChainState();

            await Dispatcher.UIThread.InvokeAsync(() => {
                Model.Blocks = state.Height;
            });
        });

        Model.ConnectedPeers = MeshNetwork.GetPeers().Count;
    }

    private async Task OnWalletUpdated(Kryolite.Shared.Wallet wallet)
    {
        if (wallet.WalletType != Shared.WalletType.WALLET)
        {
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(() => {
            Model.SetWallet(wallet);
        });
    }

    private async Task OnProgressUpdated(IList<SyncEventArgs> syncArgs)
    {
        if (syncArgs.Count == 0)
        {
            return;
        }

        var max = syncArgs.MaxBy(x => x.Progress);

        await Dispatcher.UIThread.InvokeAsync(() => {
            var syncProgress = this.FindControl<ProgressBar>("SyncProgress");

            if (syncProgress is null)
            {
                return;
            }

            if (syncProgress.IsEnabled)
            {
                syncProgress.ProgressTextFormat = $$"""{{max.Status}}: {1:0}%""";
                syncProgress.Value = max.Progress;
                syncProgress.Minimum = 0;
                syncProgress.Maximum = 100;
            }
        });
    }
}
