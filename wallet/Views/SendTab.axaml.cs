using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Kryolite.ByteSerializer;
using Kryolite.Node;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using Kryolite.Shared.Dto;
using Microsoft.Extensions.DependencyInjection;

namespace Kryolite.Wallet;

public partial class SendTab : UserControl
{
    private SendTabViewModel Model;

    public SendTab()
    {
        AvaloniaXamlLoader.Load(this);

        Model = new();
        DataContext = Model;

        Model.SendTransactionClicked += async (object? sender, EventArgs args) =>
        {
            try
            {
                using var scope = Program.ServiceCollection.CreateScope();
                var storeManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();
                var walletManager = scope.ServiceProvider.GetRequiredService<IWalletManager>();

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

                    payload = Serializer.Serialize<TransactionPayload>(transactionPayload);
                }

                var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                if (Model.IsScheduled)
                {
                    timestamp = new DateTimeOffset(DateTime.SpecifyKind(Model.Date, DateTimeKind.Local))
                        .Add(Model.Time)
                        .ToUnixTimeMilliseconds();
                }

                var transaction = new Transaction
                {
                    TransactionType = TransactionType.PAYMENT,
                    PublicKey = Model.SelectedWallet.PublicKey,
                    To = Model.Recipient,
                    Value = (ulong)(decimal.Parse(Model.Amount) * 1000000),
                    Timestamp = timestamp,
                    Data = payload
                };

                transaction.MaxFee = (uint)storeManager.GetTransactionFeeEstimate(transaction);

                if (TopLevel.GetTopLevel(this) is not Window window)
                {
                    return;
                }

                var ok = await TransactionDialog.Show(transaction, window);

                if (!ok)
                {
                    return;
                }

                transaction.Sign(walletManager.GetPrivateKey(Model.SelectedWallet!.PublicKey));

                var result = storeManager.AddTransaction(new TransactionDto(transaction), true);

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
        if (Model.Recipient == null || !Address.IsValid(Model.Recipient))
        {
            Model.Manifest = null;
            Model.Method = null;
            return;
        }

        var mInfo = sender.GetType().GetMethod("ClosingDropDown", BindingFlags.NonPublic | BindingFlags.Instance);
        mInfo?.Invoke(sender, [true]);

        var addr = (Address)Model.Recipient;

        if (!addr.IsContract())
        {
            Model.Manifest = null;
            Model.Method = null;
            return;
        }

        using var scope = Program.ServiceCollection.CreateScope();
        var blockchainManager = scope.ServiceProvider.GetService<IStoreManager>() ?? throw new ArgumentNullException(nameof(IStoreManager));

        var contract = blockchainManager.GetContract(addr);

        if (contract is null)
        {
            Model.Manifest = null;
            Model.Method = null;
            return;
        }

        Model.Manifest = new ManifestView()
        {
            Name = contract.Manifest?.Name ?? "n/a",
            Url = contract.Manifest?.Url ?? string.Empty,
            Methods = contract?.Manifest?.Methods
                .DistinctBy(x => x.Name)
                .Where(x => !x.IsReadOnly)
                .Select(x => new MethodView
                {
                    Name = x.Name,
                    Description = x.Description ?? x.Name,
                    Params = x.Params.Select(y => new ParamView
                    {
                        Name = y.Name,
                        Description = y.Description ?? y.Name
                    })
                    .ToList()
                })
                .ToList() ?? new()
        };
    }

    public void OpenUrl(object sender, PointerPressedEventArgs args)
    {
        if (Model.Manifest is null || string.IsNullOrEmpty(Model.Manifest.Url))
        {
            return;
        }

        if (!Uri.IsWellFormedUriString(Model.Manifest.Url, UriKind.Absolute))
        {
            return;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            //https://stackoverflow.com/a/2796367/241446
            using var proc = new Process { StartInfo = { UseShellExecute = true, FileName = Model.Manifest.Url } };
            proc.Start();

            return;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            Process.Start("xdg-open", Model.Manifest.Url);
            return;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            Process.Start("open", Model.Manifest.Url);
        }

        return;
    }
}
