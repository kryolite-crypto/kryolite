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
using Kryolite.Shared;
using Avalonia.Styling;

namespace Kryolite.Wallet;

public partial class MainWindow : Window
{
    private IBlockchainManager BlockchainManager;
    private IMempoolManager MempoolManager;
    private INetworkManager NetworkManager;
    private IWalletManager WalletManager;
    private IMeshNetwork MeshNetwork;

    private MainWindowViewModel Model = new MainWindowViewModel();

    public MainWindow()
    {
        BlockchainManager = Program.ServiceCollection.GetService<IBlockchainManager>() ?? throw new ArgumentNullException(nameof(IBlockchainManager));
        MempoolManager = Program.ServiceCollection.GetService<IMempoolManager>() ?? throw new ArgumentNullException(nameof(IMempoolManager));
        NetworkManager = Program.ServiceCollection.GetService<INetworkManager>() ?? throw new ArgumentNullException(nameof(INetworkManager));
        WalletManager = Program.ServiceCollection.GetService<IWalletManager>() ?? throw new ArgumentNullException(nameof(IWalletManager));
        MeshNetwork = Program.ServiceCollection.GetService<IMeshNetwork>() ?? throw new ArgumentNullException(nameof(IMeshNetwork));

        AvaloniaXamlLoader.Load(this);

#if DEBUG
        this.AttachDevTools();
#endif

        DataContext = Model;

        this.Opened += OnInitialized;

        var syncProgress = this.FindControl<ProgressBar>("SyncProgress");

        Model.ViewLogClicked += (object? sender, EventArgs args) => {
            var dialog = new LogViewerDialog();
            dialog.Show(this);
        };

        Node.MeshNetwork.ConnectedChanged += async (object? sender, int count) => {
            await Dispatcher.UIThread.InvokeAsync(() => {
                Model.ConnectedPeers = count;
            });
        };

        ChainObserver.BeginSync += async (object? sender, long total) => {
            await Dispatcher.UIThread.InvokeAsync(() => {
                syncProgress.Value = 0;
                syncProgress.Minimum = 0;
                syncProgress.Maximum = 100;
                syncProgress.IsEnabled = true;
                syncProgress.IsVisible = true;
            });
        };

        ChainObserver.SyncProgress += async (object? sender, SyncEventArgs e) => {
            await Dispatcher.UIThread.InvokeAsync(() => {
                syncProgress.ProgressTextFormat = $$"""{{e.Status}}: {1:0}%""";
                syncProgress.Value = e.Progress;
                syncProgress.Minimum = 0;
                syncProgress.Maximum = 100;

                if (!syncProgress.IsEnabled) {
                    syncProgress.IsEnabled = true;
                    syncProgress.IsVisible = true;
                }
            });
        };

        ChainObserver.EndSync += async (object? sender, EventArgs args) => {
            await Dispatcher.UIThread.InvokeAsync(() => {
                syncProgress.Value = 0;
                syncProgress.IsEnabled = false;
                syncProgress.IsVisible = false;
            });
        };

        BlockchainManager.OnChainUpdated(new ActionBlock<ChainState>(async state => {
            await Dispatcher.UIThread.InvokeAsync(() => {
                Model.Blocks = state.POW.Height;
            });
        }));

        BlockchainManager.OnWalletUpdated(new ActionBlock<Kryolite.Shared.Wallet>(async wallet => await OnWalletUpdated(wallet)));

        // TODO: Initialize these before app is fully started to not miss any?
        var transactionAddedBuffer = new BufferBlock<Transaction>(new DataflowBlockOptions { BoundedCapacity = Constant.MAX_MEMPOOL_TX });

        transactionAddedBuffer.AsObservable()
            .Buffer(TimeSpan.FromSeconds(1))
            .Subscribe(async transactions => await OnTransactionAdded(transactions));

        MempoolManager.OnTransactionAdded(transactionAddedBuffer);

        var transactionRemovedBuffer = new BufferBlock<Transaction>(new DataflowBlockOptions { BoundedCapacity = Constant.MAX_MEMPOOL_TX });

        transactionRemovedBuffer.AsObservable()
            .Buffer(TimeSpan.FromSeconds(1))
            .Subscribe(async transactions => await OnTransactionRemoved(transactions));

        MempoolManager.OnTransactionRemoved(transactionRemovedBuffer);
    }

    private void OnInitialized(object? sender, EventArgs args)
    {
        Task.Run(async () => {
            var wallets = WalletManager.GetWallets().Values
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
                Model.Blocks = state.POS.Height;
            });
        });

        Model.ConnectedPeers = MeshNetwork.GetPeers().Count;
    }

    private async Task OnWalletUpdated(Kryolite.Shared.Wallet wallet)
    {
        await Dispatcher.UIThread.InvokeAsync(() => {
            Model.SetWallet(wallet);
        });
    }

    private async Task OnTransactionAdded(IList<Transaction> transactions)
    {
        var wallets = Model.Wallets.Select(x => x.Address).ToHashSet();
        var pending = Model.Pending;

        foreach (var tx in transactions) {
            if (wallets.Contains(tx.PublicKey!.ToAddress().ToString())) {
                pending += tx.Value;
            }
        }

        await Dispatcher.UIThread.InvokeAsync(() => {
            Model.Pending = pending;
        });
    }

    private async Task OnTransactionRemoved(IList<Transaction> transactions)
    {
        var wallets = Model.Wallets.Select(x => x.Address).ToHashSet();
        var pending = Model.Pending;

        foreach (var tx in transactions) {
            if (wallets.Contains(tx.PublicKey!.ToAddress().ToString())) {
                pending -= tx.Value;
            }
        }

        await Dispatcher.UIThread.InvokeAsync(() => {
            Model.Pending = pending;
        });
    }
}
