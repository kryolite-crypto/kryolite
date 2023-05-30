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
using Redbus.Interfaces;
using System.Diagnostics;
using Kryolite.Shared;

namespace Kryolite.Wallet;

public partial class MainWindow : Window
{
    private INetworkManager NetworkManager;
    private IWalletManager WalletManager;
    private IMeshNetwork MeshNetwork;
    private IEventBus EventBus;

    private MainWindowViewModel Model = new MainWindowViewModel();

    public MainWindow()
    {
        NetworkManager = Program.ServiceCollection.GetService<INetworkManager>() ?? throw new ArgumentNullException(nameof(INetworkManager));
        WalletManager = Program.ServiceCollection.GetService<IWalletManager>() ?? throw new ArgumentNullException(nameof(IWalletManager));
        MeshNetwork = Program.ServiceCollection.GetService<IMeshNetwork>() ?? throw new ArgumentNullException(nameof(IMeshNetwork));
        EventBus = Program.ServiceCollection.GetService<IEventBus>() ?? throw new ArgumentNullException(nameof(IEventBus));

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


        var progressUpdatedBuffer = new BufferBlock<SyncProgress>();

        progressUpdatedBuffer.AsObservable()
            .Buffer(TimeSpan.FromSeconds(1), 1000)
            .Subscribe(async syncArgs => await OnProgressUpdated(syncArgs));

        EventBus.Subscribe<SyncProgress>((progress) => {
            var syncProgress = this.FindControl<ProgressBar>("SyncProgress");

            if (syncProgress is null)
            {
                return;
            }

            progressUpdatedBuffer.Post(progress);
        });

        EventBus.Subscribe<ChainState>(async state => {
            await Dispatcher.UIThread.InvokeAsync(() => {
                Model.Blocks = state.Height;
            });
        });

        EventBus.Subscribe<Shared.Wallet>(async wallet => {
            if (wallet.WalletType != WalletType.WALLET)
            {
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(() => {
                Model.SetWallet(wallet);
            });
        });
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
            using var scope = Program.ServiceCollection.CreateScope();
            var blockchainManager = scope.ServiceProvider.GetService<IBlockchainManager>() ?? throw new ArgumentNullException(nameof(IBlockchainManager));

            var state = blockchainManager.GetChainState();

            await Dispatcher.UIThread.InvokeAsync(() => {
                Model.Blocks = state.Height;
            });
        });

        Model.ConnectedPeers = MeshNetwork.GetPeers().Count;
    }

    private async Task OnProgressUpdated(IList<SyncProgress> syncArgs)
    {
        if (syncArgs.Count == 0)
        {
            return;
        }

        var max = syncArgs.MaxBy(x => x.Progress);

        if (max is null)
        {
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(() => {
            var syncProgress = this.FindControl<ProgressBar>("SyncProgress");

            if (syncProgress is null)
            {
                return;
            }

            syncProgress.Value = max.Progress;
            syncProgress.Minimum = 0;
            syncProgress.Maximum = 100;
            syncProgress.IsEnabled = !max.Completed;
            syncProgress.IsVisible = !max.Completed;
        });
    }
}
