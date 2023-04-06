using Avalonia.Controls;
using Avalonia.Interactivity;
using Kryolite.Node;
using Material.Icons;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace Kryolite.Wallet;

public partial class TokensTab : UserControl
{
    private IBlockchainManager BlockchainManager;
    private IWalletManager WalletManager;

    private TokensTabViewModel Model = new();

    public TokensTab()
    {
        BlockchainManager = Program.ServiceCollection.GetService<IBlockchainManager>() ?? throw new ArgumentNullException(nameof(IBlockchainManager));
        WalletManager = Program.ServiceCollection.GetService<IWalletManager>() ?? throw new ArgumentNullException(nameof(IWalletManager));

        InitializeComponent();
        DataContext = Model;

        var grid = this.GetControl<DataGrid>("TokensGrid");

        grid.LoadingRow += (object? sender, DataGridRowEventArgs e) => {
            var dataObject = e.Row.DataContext as TokenModel;
            if (dataObject != null && dataObject.IsConsumed)
            {
                e.Row.Classes.Add("isConsumed");
            }
            else
            {
                e.Row.Classes.Remove("isConsumed");
            }
        };

        Loaded += TokensTab_Loaded;
    }

    private void TokensTab_Loaded(object? sender, RoutedEventArgs e)
    {
        _ = Task.Run(() => {
            var wallets = WalletManager.GetWallets();
            var collection = new List<TokenModel>();

            foreach (var wallet in wallets)
            {
                var tokens = BlockchainManager.GetTokens(wallet.Key)
                    .Select(token => new TokenModel
                    {
                        Name = token.Name,
                        Description = token.Description,
                        IsConsumed = token.IsConsumed
                    });

                collection.AddRange(tokens.ToList());
            }

            Model.Tokens = new ObservableCollection<TokenModel>(collection);
        }); 
    }
}
