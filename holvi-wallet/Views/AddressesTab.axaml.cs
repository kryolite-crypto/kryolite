using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Marccacoin;
using Marccacoin.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace holvi_wallet;

public partial class AddressesTab : UserControl
{
    private IWalletManager WalletManager;
    private AddressesTabViewModel Model = new();

    public AddressesTab()
    {
        WalletManager = Program.ServiceCollection.GetService<IWalletManager>() ?? throw new ArgumentNullException(nameof(IWalletManager));

        AvaloniaXamlLoader.Load(this);

        DataContext = Model;

        var walletGrid = this.FindControl<DataGrid>("WalletsGrid");

        walletGrid.CellEditEnded += (object? sender, DataGridCellEditEndedEventArgs args) => {
            if (args.Row.DataContext is WalletModel walletModel) {
                walletModel.Wallet.Description = walletModel.Description;
                WalletManager.UpdateWallet(walletModel.Wallet);
            }
        };

        Model.NewAddressClicked += async (object? sender, EventArgs args) => {
            var wallet = WalletManager.CreateWallet();

            await Dispatcher.UIThread.InvokeAsync(() => {
                if (this.VisualRoot is MainWindow mw)
                if (mw.DataContext is MainWindowViewModel model) {
                    model.SetWallet(wallet);
                }
            });
        };

        Model.CopyAddressClicked += async (object? sender, EventArgs args) => {
            var wallet = (WalletModel)walletGrid.SelectedItem;
            await Application.Current!.Clipboard!.SetTextAsync(wallet.Address ?? "");
        };
    }
}