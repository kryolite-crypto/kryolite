using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace Kryolite.Wallet;

public partial class ConfirmDialog : Window
{
    public ConfirmDialog()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public static async Task<bool> Show(string text, Window owner)
    {
        var dialog = new ConfirmDialog();
        dialog.Opened += async (object? sender, EventArgs e) =>
        {
            var textContainer = dialog.FindControl<TextBlock>("TextContainer");

            await Dispatcher.UIThread.InvokeAsync(() => {
                if (textContainer is not null)
                {
                    textContainer.Text = text;
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
}
