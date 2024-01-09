using System;
using System.ComponentModel;
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
using Kryolite.Shared.Blockchain;
using Kryolite.Shared.Dto;
using MessagePack;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Immutable;
using Avalonia.VisualTree;

namespace Kryolite.Wallet;

public partial class SendTab : UserControl
{
    private SendTabViewModel Model;

    public SendTab()
    {
        AvaloniaXamlLoader.Load(this);
        
        Model = new();
        DataContext = Model;

        Model.SendTransactionClicked += async (object? sender, EventArgs args) => {
            try
            {
                using var scope = Program.ServiceCollection.CreateScope();
                var blockchainManager = scope.ServiceProvider.GetService<IStoreManager>() ?? throw new ArgumentNullException(nameof(IStoreManager));

                if (Model.Recipient is null || Model.SelectedWallet is null || Model.Amount is null || !Address.IsValid(Model.Recipient))
                {
                    return;
                }

                var payload = Array.Empty<byte>();

                if (((Address)Model.Recipient).IsContract())
                {
                    if (Model.Method is null)
                    {
                        return;
                    }

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

                    payload = MessagePackSerializer.Serialize(transactionPayload, lz4Options);
                }

                var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                if (Model.IsScheduled)
                {
                    timestamp = new DateTimeOffset(DateTime.SpecifyKind(Model.Date, DateTimeKind.Local))
                        .Add(Model.Time)
                        .ToUnixTimeMilliseconds();
                }

                var transaction = new Transaction {
                    TransactionType = TransactionType.PAYMENT,
                    PublicKey = Model.SelectedWallet.PublicKey,
                    To = Model.Recipient,
                    Value = (ulong)(decimal.Parse(Model.Amount) * 1000000),
                    Timestamp = timestamp,
                    Data = payload
                };

                if(TopLevel.GetTopLevel(this) is not Window window)
                {
                    return;
                }

                var ok = await TransactionDialog.Show(transaction, window);

                if (!ok)
                {
                    return;
                }

                transaction.Sign(Model.SelectedWallet!.PrivateKey);

                var result = blockchainManager.AddTransaction(new TransactionDto(transaction), true);

                if (!Model.Addresses.Contains(Model.Recipient))
                {
                    Model.Addresses.Add(Model.Recipient);
                }

                Model.SelectedWallet = null;
                Model.Recipient = null;
                Model.Amount = null;
                Model.IsScheduled = false;

                await ConfirmDialog.Show($"Transaction Status: {result}", true, window);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
        };
    }

    public void RecipientGotFocus(object sender, GotFocusEventArgs args)
    {
        var box = (AutoCompleteBox)sender;

        if (string.IsNullOrEmpty(box.Text) && Model.Addresses.Count > 0)
        {
            var mInfo = sender.GetType().GetMethod("OpeningDropDown", BindingFlags.NonPublic | BindingFlags.Instance);
            mInfo?.Invoke(sender, [false]);
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
        mInfo?.Invoke(sender, [true]);

        var addr = (Address)Model.Recipient;

        if (!addr.IsContract())
        {
            container.IsVisible = false;
            return;
        }

        using var scope = Program.ServiceCollection.CreateScope();
        var blockchainManager = scope.ServiceProvider.GetService<IStoreManager>() ?? throw new ArgumentNullException(nameof(IStoreManager));

        var contract = blockchainManager.GetContract(addr);

        Model.Manifest = new ManifestView()
        {
            Name = contract?.Manifest.Name ?? string.Empty,
            Methods = contract?.Manifest.Methods
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
