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

    private MainWindowViewModel Model = new MainWindowViewModel();

    public MainWindow()
    {
        BlockchainManager = Program.ServiceCollection.GetService<IBlockchainManager>() ?? throw new ArgumentNullException(nameof(IBlockchainManager));
        MempoolManager = Program.ServiceCollection.GetService<IMempoolManager>() ?? throw new ArgumentNullException(nameof(IMempoolManager));

        InitializeComponent();

        DataContext = Model;

        var wallets = BlockchainManager.GetWallets()
            .Select(wallet => new WalletModel {
                Description = wallet.Description,
                Address = wallet.Address,
                PublicKey = wallet.PublicKey,
                PrivateKey = wallet.PrivateKey,
                Balance = wallet.Balance,
                WalletTransactions = wallet.WalletTransactions
            });

        Model.Wallets = new ObservableCollection<WalletModel>(wallets);

        var walletGrid = this.FindControl<DataGrid>("WalletsGrid");
        var buffer = new BufferBlock<List<Transaction>>();

        walletGrid.CellEditEnded += (object? sender, DataGridCellEditEndedEventArgs args) => {
            if (args.Row.DataContext is Wallet wallet) {
                BlockchainManager.UpdateWallet(wallet);
            }
        };

        Model.NewAddressClicked += async (object? sender, EventArgs args) => {
            var wallet = BlockchainManager.CreateWallet();

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

        Model.ViewLogClicked += async (object? sender, EventArgs args) => {
            var dialog = new LogViewerDialog();
            dialog.Show(this);
        };

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
