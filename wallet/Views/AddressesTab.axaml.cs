using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Kryolite.Node;
using Microsoft.Extensions.DependencyInjection;

namespace Kryolite.Wallet;

public partial class AddressesTab : UserControl
{
    private IWalletManager WalletManager;
    private AddressesTabViewModel Model;

    public AddressesTab()
    {
        var scope = Program.ServiceCollection.CreateScope();
        WalletManager = scope.ServiceProvider.GetService<IWalletManager>() ?? throw new ArgumentNullException(nameof(IWalletManager));

        AvaloniaXamlLoader.Load(this);

        Model = new();
        DataContext = Model;

        var walletGrid = this.FindControl<DataGrid>("WalletsGrid");

        if (walletGrid is null)
        {
            throw new Exception("Addresses tab initialization failed");
        }

        walletGrid.CellEditEnded += (object? sender, DataGridCellEditEndedEventArgs args) => {
            if (args.Row.DataContext is WalletModel walletModel) {
                WalletManager.UpdateDescription(walletModel.Address, walletModel.Description ?? string.Empty);
            }
        };

        Model.NewAddressClicked += async (object? sender, EventArgs args) => {
            var wallet = WalletManager.CreateWallet();

            await Dispatcher.UIThread.InvokeAsync(() => {
                if (this.VisualRoot is MainWindow mw)
                {
                    mw.Wallets.TryAdd(wallet.Address, wallet);
                }

                Model.State.AddWallet(wallet);
            });
        };

        Model.CopyAddressClicked += async (object? sender, EventArgs args) => {
            var wallet = (WalletModel)walletGrid.SelectedItem;
            TopLevel.GetTopLevel(this)?.Clipboard!.SetTextAsync(wallet.Address.ToString() ?? "");
            await Task.CompletedTask;
        };
    }
}
