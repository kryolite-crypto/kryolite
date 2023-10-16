using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Kryolite.EventBus;
using Kryolite.Node;
using Microsoft.Extensions.DependencyInjection;
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
    private IWalletManager WalletManager;
    private IEventBus EventBus;

    private TokensTabViewModel Model = new();

    public TokensTab()
    {
        var scope = Program.ServiceCollection.CreateScope();
        WalletManager = scope.ServiceProvider.GetService<IWalletManager>() ?? throw new ArgumentNullException(nameof(IWalletManager));
        EventBus = Program.ServiceCollection.GetService<IEventBus>() ?? throw new ArgumentNullException(nameof(IWalletManager));

        AvaloniaXamlLoader.Load(this);
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

        this.AttachedToVisualTree += InitializeData;

        //InitializeData();

        /*var tokenTransferredBuffer = new BufferBlock<TransferTokenEventArgs>();

        tokenTransferredBuffer.AsObservable()
            .Buffer(TimeSpan.FromMilliseconds(1000), 100)
            .Subscribe(async tokens => {
                if (tokens.Count() == 0)
                {
                    return;
                }

                using var scope = Program.ServiceCollection.CreateScope();
                var blockchainManager = scope.ServiceProvider.GetService<IStoreManager>() ?? throw new ArgumentNullException(nameof(IStoreManager));

                var wallets = WalletManager.GetWallets();

                foreach (var token in tokens)
                {
                    var exists = Model.Tokens
                        .Where(t => t.TokenId == token.TokenId)
                        .FirstOrDefault();

                    if (exists is null)
                    {
                        // Query token from db and add to model
                        var newToken = blockchainManager.GetToken(token.Contract, token.TokenId);

                        if (newToken is null)
                        {
                            continue;
                        }

                        await Dispatcher.UIThread.InvokeAsync(() => {
                            Model.Tokens.Add(new TokenModel
                            {
                                TokenId = newToken.TokenId,
                                Owner = newToken.Ledger,
                                Name = newToken.Name,
                                Description = newToken.Description,
                                IsConsumed = newToken.IsConsumed
                            });
                        });

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

        EventBus.Subscribe<TransferTokenEventArgs>(e => tokenTransferredBuffer.Post(e));

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

        EventBus.Subscribe<ConsumeTokenEventArgs>(e => tokenConsumedBuffer.Post(e));*/
    }

    private void InitializeData(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _ = Task.Run(() => {
            var wallets = WalletManager.GetWallets();
            var collection = new List<TokenModel>();

            using var scope = Program.ServiceCollection.CreateScope();
            var blockchainManager = scope.ServiceProvider.GetService<IStoreManager>() ?? throw new ArgumentNullException(nameof(IStoreManager));

            foreach (var wallet in wallets)
            {
                var tokens = blockchainManager.GetTokens(wallet.Key)
                    .Select(token => new TokenModel
                    {
                        TokenId = token.TokenId,
                        Owner = token.Ledger,
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
