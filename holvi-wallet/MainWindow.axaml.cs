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

namespace holvi_wallet;

public partial class MainWindow : Window
{
    private IBlockchainManager BlockchainManager;
    private MainWindowViewModel Model = new MainWindowViewModel();

    public MainWindow()
    {
        BlockchainManager = Program.ServiceCollection.GetService<IBlockchainManager>() ?? throw new ArgumentNullException(nameof(IBlockchainManager));

        InitializeComponent();

        DataContext = Model;

        var wallets = BlockchainManager.GetWallets();
        Model.Wallets = new ObservableCollection<Wallet>(wallets);

        var walletGrid = this.FindControl<DataGrid>("WalletsGrid");

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

        BlockchainManager.OnWalletUpdated(new ActionBlock<Wallet>(async wallet => {
            await Dispatcher.UIThread.InvokeAsync(() => {
                Model.SetWallet(wallet);
            });
        }));
    }
}
