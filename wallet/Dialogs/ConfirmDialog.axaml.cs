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

    public static async Task<bool> Show(string text, bool okOnly, Window owner)
    {
        var dialog = new ConfirmDialog();

        dialog.Opened += async (object? sender, EventArgs e) =>
        {
            var textContainer = dialog.FindControl<TextBlock>("TextContainer");
            var cancelButton = dialog.FindControl<Button>("CancelButton");

            if (okOnly && cancelButton is not null)
            {
                await Dispatcher.UIThread.InvokeAsync(() => {
                    cancelButton.IsVisible = false;
                });
            }

            if (textContainer is not null)
            {
                await Dispatcher.UIThread.InvokeAsync(() => {
                    textContainer.Text = text;   
                });
            }
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
