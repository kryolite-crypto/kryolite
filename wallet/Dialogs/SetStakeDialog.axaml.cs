using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Kryolite.Shared;

namespace Kryolite.Wallet;

public partial class SetStakeDialog : Window
{
    public SetStakeDialog()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public static async Task<TransferModel?> Show(string title, TransferModel model, Window owner)
    {
        var dialog = new SetStakeDialog
        {
            DataContext = model
        };

        dialog.Opened += async (object? sender, EventArgs e) =>
        {
            await Dispatcher.UIThread.InvokeAsync(() => {
                dialog.Title = title;
            });
        };

        return await dialog.ShowDialog<TransferModel?>(owner);
    }

    public void Ok(object sender, RoutedEventArgs args)
    {
        Close(DataContext);
    }

    public void Cancel(object sender, RoutedEventArgs args)
    {
        Close(null);
    }
}
