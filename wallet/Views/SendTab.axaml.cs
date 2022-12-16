using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Kryolite.Node;
using Kryolite.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace Kryolite.Wallet;

public partial class SendTab : UserControl
{
    private IBlockchainManager BlockchainManager;
    private SendTabViewModel Model = new();

    public SendTab()
    {
        BlockchainManager = Program.ServiceCollection.GetService<IBlockchainManager>() ?? throw new ArgumentNullException(nameof(IBlockchainManager));

        AvaloniaXamlLoader.Load(this);
        DataContext = Model;

        Model.SendTransactionClicked += (object? sender, EventArgs args) => {
            var transaction = new Transaction {
                TransactionType = TransactionType.PAYMENT,
                PublicKey = Model.SelectedWallet!.PublicKey,
                To = Model.Recipient!,
                Value = (ulong)(decimal.Parse(Model.Amount!) * 1000000),
                MaxFee = 1,
                Nonce = (new Random()).Next()
            };

            transaction.Sign(Model.SelectedWallet!.PrivateKey);

            BlockchainManager.AddTransactionsToQueue(transaction);

            Model.Recipient = "";
            Model.Amount = "";
        };
    }
}
