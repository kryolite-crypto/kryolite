using System;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Kryolite.Node;
using Kryolite.Shared;
using MessagePack;
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

            if (transaction.To.IsContract())
            {
                var transactionPayload = new TransactionPayload
                {
                    Payload = new CallMethod
                    {
                        Method = Model.Method.Name,
                        Params = Model.Method.Params.Select(x => x.Value).ToArray()
                    }
                };

                var lz4Options = MessagePackSerializerOptions.Standard
                    .WithCompression(MessagePackCompression.Lz4BlockArray)
                    .WithOmitAssemblyVersion(true);

                transaction.Data = MessagePackSerializer.Serialize(transactionPayload, lz4Options);
            }

            transaction.Sign(Model.SelectedWallet!.PrivateKey);

            BlockchainManager.AddTransactionsToQueue(transaction);

            if (!Model.Addresses.Contains(Model.Recipient!))
            {
                Model.Addresses.Add(Model.Recipient!);
            }

            Model.Recipient = "";
            Model.Amount = "";
        };
    }

    public void RecipientGotFocus(object sender, GotFocusEventArgs args)
    {
        var box = (AutoCompleteBox)sender;

        if (string.IsNullOrEmpty(box.Text) && Model.Addresses.Count > 0)
        {
            var mInfo = sender.GetType().GetMethod("OpeningDropDown", BindingFlags.NonPublic | BindingFlags.Instance);
            mInfo?.Invoke(sender, new object[] { false });
        }
    }

    public void RecipientChanged(object sender, TextChangedEventArgs args)
    {
        var container = this.GetControl<GroupBox>("MethodContainer");

        if (Model.Recipient == null || !Address.IsValid(Model.Recipient))
        {
            container.IsVisible = false;
            return;
        }

        var mInfo = sender.GetType().GetMethod("ClosingDropDown", BindingFlags.NonPublic | BindingFlags.Instance);
        mInfo?.Invoke(sender, new object[] { true });

        var addr = (Address)Model.Recipient;

        if (!addr.IsContract())
        {
            container.IsVisible = false;
            return;
        }

        var contract = BlockchainManager.GetContract(addr);

        Model.Manifest = new ManifestView()
        {
            Name = contract?.Manifest.Name ?? string.Empty,
            Methods = contract?.Manifest.Methods
                .Where(x => !x.IsReadonly)
                .DistinctBy(x => x.Name)
                .Select(x => new MethodView
                {
                    Name = x.Name,
                    Params = x.Params.Select(y => new ParamView
                    {
                        Name = y.Name
                    })
                    .ToList()
                })
                .ToList() ?? new()
        };

        container.IsVisible = true;
    }
}
