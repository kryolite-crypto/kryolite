using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Kryolite.Interface;
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
            if (args.Row.DataContext is AccountModel walletModel) {
                WalletManager.UpdateDescription(walletModel.Address, walletModel.Description ?? string.Empty);
            }
        };

        Model.NewAddressClicked += async (object? sender, EventArgs args) => {
            var account = WalletManager.CreateAccount();

            await Dispatcher.UIThread.InvokeAsync((Action)(() => {
                if (this.VisualRoot is MainWindow mw)
                {
                    mw.Accounts.TryAdd(account.Address, account);
                }

                Model.State.AddAccount(account);
            }));
        };

        Model.CopyAddressClicked += async (object? sender, EventArgs args) => {
            var wallet = (AccountModel)walletGrid.SelectedItem;
            TopLevel.GetTopLevel(this)?.Clipboard!.SetTextAsync(wallet.Address.ToString() ?? "");
            await Task.CompletedTask;
        };
    }
}
