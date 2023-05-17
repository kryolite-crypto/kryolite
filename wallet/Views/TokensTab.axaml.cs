using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Kryolite.Node;
using Kryolite.Shared;
using Material.Icons;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

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

        InitializeData();

        var tokenTransferredBuffer = new BufferBlock<TransferTokenEventArgs>();

        tokenTransferredBuffer.AsObservable()
            .Buffer(TimeSpan.FromMilliseconds(1000), 100)
            .Subscribe(async tokens => {
                if (tokens.Count() == 0)
                {
                    return;
                }

                var wallets = WalletManager.GetWallets();

                foreach (var token in tokens)
                {
                    var exists = Model.Tokens
                        .Where(t => t.TokenId == token.TokenId)
                        .FirstOrDefault();

                    if (exists is null)
                    {
                        // Query token from db and add to model
                        /*var newToken = BlockchainManager.GetToken(token.TokenId);

                        if (newToken is null)
                        {
                            continue;
                        }

                        await Dispatcher.UIThread.InvokeAsync(() => {
                            Model.Tokens.Add(new TokenModel
                            {
                                TokenId = newToken.TokenId,
                                Owner = newToken.Contract.Owner,
                                Name = newToken.Name,
                                Description = newToken.Description,
                                IsConsumed = newToken.IsConsumed
                            });
                        });*/
                        continue;
                    }

                    var from = token.From.ToString();
                    var to = token.To.ToString();

                    if (wallets.ContainsKey(from) && !wallets.ContainsKey(to))
                    {
                        // Token transferred out of wallet
                        await Dispatcher.UIThread.InvokeAsync(() => {
                            Model.Tokens.Remove(exists);
                        });
                        continue;
                    }

                    // token transferred from owned wallet to another, only update owner address
                    await Dispatcher.UIThread.InvokeAsync(() => {
                        exists.Owner = to;
                    });
                }
            });

        BlockchainManager.OnTokenTransferred(tokenTransferredBuffer);

        var tokenConsumedBuffer = new BufferBlock<ConsumeTokenEventArgs>();

        tokenConsumedBuffer.AsObservable()
            .Buffer(TimeSpan.FromMilliseconds(1000), 100)
            .Subscribe(async tokens => {
                if (tokens.Count() == 0)
                {
                    return;
                }

                foreach (var token in tokens)
                {
                    var exists = Model.Tokens
                        .Where(t => t.TokenId == token.TokenId)
                        .FirstOrDefault();

                    if (exists is null)
                    {
                        continue;
                    }

                    await Dispatcher.UIThread.InvokeAsync(() => {
                        exists.IsConsumed = true;
                    });
                }
            });

        BlockchainManager.OnTokenConsumed(tokenConsumedBuffer);
    }

    private void InitializeData()
    {
        _ = Task.Run(() => {
            var wallets = WalletManager.GetWallets();
            var collection = new List<TokenModel>();

            foreach (var wallet in wallets)
            {
                /*var tokens = BlockchainManager.GetTokens(wallet.Key)
                    .Select(token => new TokenModel
                    {
                        TokenId = token.TokenId,
                        Owner = token.Contract.Owner,
                        Name = token.Name,
                        Description = token.Description,
                        IsConsumed = token.IsConsumed
                    });

                collection.AddRange(tokens.ToList());*/
            }

            Model.Tokens = new ObservableCollection<TokenModel>(collection);
        }); 
    }
}
