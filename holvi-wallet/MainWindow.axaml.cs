using Avalonia.Controls;
using System;
using Microsoft.Extensions.DependencyInjection;
using Marccacoin;
using Marccacoin.Shared;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Logging;
using Avalonia.Threading;
using System.Threading.Tasks.Dataflow;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using Avalonia;
using System.Collections.Generic;

namespace holvi_wallet;

public partial class MainWindow : Window
{
    private IBlockchainManager BlockchainManager;
    private IMempoolManager MempoolManager;
    private INetworkManager NetworkManager;
    private IWalletManager WalletManager;

    private MainWindowViewModel Model = new MainWindowViewModel();

    public MainWindow()
    {
        BlockchainManager = Program.ServiceCollection.GetService<IBlockchainManager>() ?? throw new ArgumentNullException(nameof(IBlockchainManager));
        MempoolManager = Program.ServiceCollection.GetService<IMempoolManager>() ?? throw new ArgumentNullException(nameof(IMempoolManager));
        NetworkManager = Program.ServiceCollection.GetService<INetworkManager>() ?? throw new ArgumentNullException(nameof(INetworkManager));
        WalletManager = Program.ServiceCollection.GetService<IWalletManager>() ?? throw new ArgumentNullException(nameof(IWalletManager));

        InitializeComponent();

        DataContext = Model;

        this.Activated += (object? sender, EventArgs args) => {
            Task.Run(async () => {
                var wallets = WalletManager.GetWallets().Values
                    .Select(wallet => new WalletModel {
                        Description = wallet.Description,
                        Address = wallet.Address,
                        PublicKey = wallet.PublicKey,
                        PrivateKey = wallet.PrivateKey,
                        Balance = wallet.Balance,
                        WalletTransactions = wallet.WalletTransactions
                    });

                await Dispatcher.UIThread.InvokeAsync(() => {
                    Model.Wallets = new ObservableCollection<WalletModel>(wallets);
                });                    
            });

            Task.Run(async () => {
                var blocks = BlockchainManager.GetCurrentHeight();

                await Dispatcher.UIThread.InvokeAsync(() => {
                    Model.Blocks = blocks;
                });
            });

            Model.ConnectedPeers = NetworkManager.GetHostCount();
        };

        var syncProgress = this.FindControl<ProgressBar>("SyncProgress");
        var walletGrid = this.FindControl<DataGrid>("WalletsGrid");
        var buffer = new BufferBlock<List<Transaction>>();

        walletGrid.CellEditEnded += (object? sender, DataGridCellEditEndedEventArgs args) => {
            if (args.Row.DataContext is Wallet wallet) {
                WalletManager.UpdateWallet(wallet);
            }
        };

        Model.NewAddressClicked += async (object? sender, EventArgs args) => {
            var wallet = WalletManager.CreateWallet();

            await Dispatcher.UIThread.InvokeAsync(() => {
                Model.SetWallet(wallet);
            });
        };

        Model.CopyAddressClicked += async (object? sender, EventArgs args) => {
            var wallet = (WalletModel)walletGrid.SelectedItem;
            await Application.Current!.Clipboard!.SetTextAsync(wallet.Address ?? "");
        };

        Model.SendTransactionClicked += (object? sender, EventArgs args) => {
            var transaction = new Transaction {
                TransactionType = TransactionType.PAYMENT,
                PublicKey = Model.SelectedWallet!.PublicKey,
                To = Model.Recipient!,
                Value = (ulong)(decimal.Parse(Model.Amount!) * 1000000),
                MaxFee = 1,
                Nonce = (new Random()).Next()
            };

            transaction.Sign(Model.SelectedWallet.PrivateKey);

            BlockchainManager.AddTransactionsToQueue(transaction);

            Model.Recipient = "";
            Model.Amount = "";
        };

        Model.ViewLogClicked += (object? sender, EventArgs args) => {
            var dialog = new LogViewerDialog();
            dialog.Show(this);
        };

        Marccacoin.Network.ConnectedChanged += async (object? sender, int count) => {
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

        ChainObserver.SyncProgress += async (object? sender, double progress) => {
            await Dispatcher.UIThread.InvokeAsync(() => {
                syncProgress.Value = progress;
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

        // TODO: Move to WalletManager
        BlockchainManager.OnWalletUpdated(new ActionBlock<Wallet>(async wallet => {
            var pending = 0UL;

            foreach (var w in Model.Wallets) {
                pending += MempoolManager.GetPending(w.Address!);
            }

            await Dispatcher.UIThread.InvokeAsync(() => {
                Model.SetWallet(wallet);
                Model.Pending = pending;
            });
        }));

        BlockchainManager.OnBlockAdded(new ActionBlock<Block>(async block => {
            await Dispatcher.UIThread.InvokeAsync(() => {
                Model.Blocks = block.Id;
            });
        }));

        Task.Run(async () => {
            var wallets = Model.Wallets.Select(x => x.Address).ToHashSet();

            while(true) {
                await buffer.OutputAvailableAsync();

                if(buffer.TryReceive(txs => txs.Any(tx => wallets.Contains(tx.PublicKey!.Value.ToAddress().ToString())), out var _)) {
                    var pending = 0UL;

                    foreach (var w in Model.Wallets) {
                        pending += MempoolManager.GetPending(w.Address!);
                    }

                    await Dispatcher.UIThread.InvokeAsync(() => {
                        Model.Pending = pending;
                    });
                }
            }
        });

        MempoolManager.OnTransactionAdded(buffer);
    }
}
