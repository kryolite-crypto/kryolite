using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;

namespace Kryolite.Wallet;

public partial class TransactionDialog : Window
{
    public TransactionDialog()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public static async Task<bool> Show(Transaction tx, Window owner)
    {
        var dialog = new TransactionDialog();

        dialog.Opened += async (object? sender, EventArgs e) =>
        {
            var transactionIdContainer = dialog.FindControl<TextBlock>("TransactionId");
            var toContainer = dialog.FindControl<TextBlock>("To");
            var valueContainer = dialog.FindControl<TextBlock>("Value");
            var feeContainer = dialog.FindControl<TextBlock>("Fee");
            var totalContainer = dialog.FindControl<TextBlock>("Total");

            await Dispatcher.UIThread.InvokeAsync(() => {
                if (transactionIdContainer is not null)
                {
                    transactionIdContainer.Text = tx.CalculateHash().ToString();
                }

                if (toContainer is not null)
                {
                    toContainer.Text = tx.To.ToString();
                }

                if (valueContainer is not null)
                {
                    valueContainer.Text = $"{tx.Value / (decimal)Constant.DECIMAL_MULTIPLIER} KRYO";
                }

                if (feeContainer is not null)
                {
                    feeContainer.Text = $"{tx.MaxFee / (decimal)Constant.DECIMAL_MULTIPLIER} KRYO";
                }

                if (totalContainer is not null)
                {
                    totalContainer.Text = $"{(tx.Value + tx.MaxFee) / (decimal)Constant.DECIMAL_MULTIPLIER} KRYO";
                }
            });
        };

        return await dialog.ShowDialog<bool>(owner);
    }

    public void Ok(object sender, RoutedEventArgs args)
    {
        Close(true);
    }

    public void Cancel(object sender, RoutedEventArgs args)
    {
        Close(false);
    }

    public void CopyAddress(object sender, PointerPressedEventArgs args)
    {
        if (sender is TextBlock block)
        {
            TopLevel.GetTopLevel(this)?.Clipboard!.SetTextAsync(block.Text);
        }
    }
}
