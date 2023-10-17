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
using System.Diagnostics;
using Kryolite.Shared;
using System.Collections.Concurrent;
using Avalonia.Logging;
using Kryolite.EventBus;

namespace Kryolite.Wallet;

public partial class MainWindow : Window
{
    private INetworkManager NetworkManager;
    private IWalletManager WalletManager;
    private IStoreManager StoreManager;
    private IMeshNetwork MeshNetwork;
    private IEventBus EventBus;

    private MainWindowViewModel Model = new MainWindowViewModel();

    public ConcurrentDictionary<Address, Shared.Wallet> Wallets;

    public MainWindow()
    {
        var scope = Program.ServiceCollection.CreateScope();
        NetworkManager = scope.ServiceProvider.GetService<INetworkManager>() ?? throw new ArgumentNullException(nameof(INetworkManager));
        WalletManager = scope.ServiceProvider.GetService<IWalletManager>() ?? throw new ArgumentNullException(nameof(IWalletManager));
        StoreManager = scope.ServiceProvider.GetService<IStoreManager>() ?? throw new ArgumentNullException(nameof(IStoreManager));
        MeshNetwork = Program.ServiceCollection.GetService<IMeshNetwork>() ?? throw new ArgumentNullException(nameof(IMeshNetwork));
        EventBus = Program.ServiceCollection.GetService<IEventBus>() ?? throw new ArgumentNullException(nameof(IEventBus));

        AvaloniaXamlLoader.Load(this);

#if DEBUG
        // this.AttachDevTools();
#endif
        Wallets = new (WalletManager.GetWallets());

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
                Model.Blocks = state.Id;
            });
        });

        EventBus.Subscribe<Ledger>(async ledger => {
            if (!Wallets.ContainsKey(ledger.Address))
            {
                return;
            }

            var wallet = Wallets[ledger.Address];

            var transactions = StoreManager.GetLastNTransctions(wallet.Address, 5);
            var txs = transactions.Select(x =>
            {
                var isRecipient = Wallets.ContainsKey(x.To!);

                var tm = new TransactionModel
                {
                    Recipient = x.To!,
                    Amount = isRecipient ? (long)x.Value : -(long)x.Value,
                    Timestamp = x.Timestamp
                };

                return tm;
            }).ToList();

            await Dispatcher.UIThread.InvokeAsync(() => {
                Model.UpdateWallet(ledger, txs);
            });
        });
    }

    private void OnInitialized(object? sender, EventArgs args)
    {
        Task.Run(async () => {
            try
            {
                var toAdd = new List<WalletModel>();

                foreach (var wallet in Wallets.Values)
                {
                    var ledger = StoreManager.GetLedger(wallet.Address);
                    var txs = StoreManager.GetLastNTransctions(wallet.Address, 5);

                    var wm = new WalletModel
                    {
                        Description = wallet.Description,
                        Address = wallet.Address.ToString(),
                        PublicKey = wallet.PublicKey,
                        PrivateKey = wallet.PrivateKey,
                        Balance = ledger?.Balance ?? 0,
                        Pending = ledger?.Pending ?? 0,
                        Transactions = txs.Select(x =>
                        {
                            var isSender = x.From == wallet.Address;

                            var tm = new TransactionModel
                            {
                                Recipient = isSender ? x.From! : x.To!,
                                Amount = isSender ? -(long)x.Value : (long)x.Value,
                                Timestamp = x.Timestamp
                            };

                            return tm;
                        }).ToList()
                    };

                    toAdd.Add(wm);
                }

                var balance = toAdd.Sum(x => (long)(x.Balance ?? 0));

                var transactions = toAdd
                    .SelectMany(wallet => wallet.Transactions)
                    .OrderByDescending(tx => tx.Timestamp)
                    .Take(5)
                    .ToList();

                await Dispatcher.UIThread.InvokeAsync(() => {
                    Model.Wallets = new ObservableCollection<WalletModel>(toAdd);
                    Model.Balance = balance;
                    Model.Transactions = transactions;
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
        });

        Task.Run(async () => {
            using var scope = Program.ServiceCollection.CreateScope();
            var blockchainManager = scope.ServiceProvider.GetService<IStoreManager>() ?? throw new ArgumentNullException(nameof(IStoreManager));

            var state = blockchainManager.GetChainState();

            await Dispatcher.UIThread.InvokeAsync(() => {
                Model.Blocks = state.Id;
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
