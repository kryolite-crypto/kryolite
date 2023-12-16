using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace Kryolite.Wallet;

public partial class InputDialog : Window
{
    public InputModel Model { get; private set; }

    public InputDialog()
    {
        AvaloniaXamlLoader.Load(this);

        Model = new();
        DataContext = Model;
    }

    public static async Task<string?> Show(string title, string description, Window owner)
    {
        var dialog = new InputDialog();

        dialog.Opened += async (object? sender, EventArgs e) =>
        {
            await Dispatcher.UIThread.InvokeAsync(() => {
                dialog.Title = title;
                dialog.Model.Description = description;
            });
        };

        return await dialog.ShowDialog<string?>(owner);
    }

    public void Ok(object sender, RoutedEventArgs args)
    {
        Close(Model.Input);
    }

    public void Cancel(object sender, RoutedEventArgs args)
    {
        Close(null);
    }
}
