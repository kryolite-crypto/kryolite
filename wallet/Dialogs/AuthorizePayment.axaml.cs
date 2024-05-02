using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Kryolite.Shared;

namespace Kryolite.Wallet;

public partial class AuthorizePaymentDialog : Window
{
    public AuthorizePaymentDialog()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public static async Task<AccountModel?> Show(string method, List<ParamModel> methodParams, ulong amount, ulong fee, Contract contract, ObservableCollection<AccountModel> wallets, Window owner)
    {
        AuthorizePaymentDialog? dialog = null;

        await Dispatcher.UIThread.InvokeAsync(() => {
            dialog = new AuthorizePaymentDialog();
        });

        if (dialog is null)
        {
            return null;
        }

        dialog.Opened += async (object? sender, EventArgs e) =>
        {
            var bMethod = dialog.FindControl<TextBlock>("MethodName");
            var bParams = dialog.FindControl<ItemsRepeater>("MethodParams");
            var bValue = dialog.FindControl<TextBlock>("Value");
            var bContract = dialog.FindControl<TextBlock>("Name");
            var bAddress = dialog.FindControl<TextBlock>("Address");
            var bUrl = dialog.FindControl<TextBlock>("Url");
            var bWallets = dialog.FindControl<ComboBox>("Wallets");
            var bFee = dialog.FindControl<TextBlock>("Fee");
            var bTotal = dialog.FindControl<TextBlock>("Total");

            await Dispatcher.UIThread.InvokeAsync(() => {
                bMethod!.Text = method;
                bParams!.ItemsSource = methodParams;
                bValue!.Text = $"{(double)amount / Constant.DECIMAL_MULTIPLIER} kryo";
                bContract!.Text = contract.Name;
                bAddress!.Text = contract.Address.ToString();
                bUrl!.Text = contract.Manifest.Url;
                bWallets!.ItemsSource = wallets;
                bFee!.Text = $"{fee / (decimal)Constant.DECIMAL_MULTIPLIER} KRYO";
                bTotal!.Text = $"{(amount + fee) / (decimal)Constant.DECIMAL_MULTIPLIER} KRYO";
            });
        };

        AccountModel? wallet = null;

        await Dispatcher.UIThread.InvokeAsync(async () => {
            wallet = await dialog.ShowDialog<AccountModel?>(owner);
        });

        return wallet;
    }

    public void Ok(object sender, RoutedEventArgs args)
    {
        if (sender is not Button button)
        {
            Close(null);
            return;
        }

        var bWallet = this.FindControl<ComboBox>("Wallets");

        if (bWallet?.SelectedItem is not AccountModel wallet)
        {
            Close(null);
            return;
        }

        Close(wallet);
    }

    public void Cancel(object sender, RoutedEventArgs args)
    {
        Close(null);
    }
}

public class ParamModel
{
    public string Name { get; set; }= string.Empty;
    public string Value { get; set; }= string.Empty;
}
