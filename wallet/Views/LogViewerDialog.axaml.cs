using System;
using System.ComponentModel;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace Kryolite.Wallet;

public partial class LogViewerDialog : Window
{
    private LogViewerDialogViewModel Model = new ();
    private BufferBlock<string> LogBuffer = new();

    public LogViewerDialog()
    {
        AvaloniaXamlLoader.Load(this);
        DataContext = Model;

        var logBox = this.FindControl<TextBox>("LogBox");

        logBox.Text = String.Join(Environment.NewLine, InMemoryLogger.Messages.ToArray()) + Environment.NewLine;
        logBox.CaretIndex = logBox.Text.Length;

        EventHandler<string> MessageHandler = delegate(object? sender, string message)
        {
            LogBuffer.Post(message);
        };

        InMemoryLogger.OnNewMessage += MessageHandler;

        LogBuffer.AsObservable()
            .Buffer(TimeSpan.FromSeconds(1))
            .Subscribe(async messages => {
                if (messages.Count == 0)
                {
                    return;
                }

                var newText = String.Join(Environment.NewLine, messages) + Environment.NewLine;

                await Dispatcher.UIThread.InvokeAsync(() => {
                    logBox.Text += newText;

                    if (!logBox.IsFocused)
                    {
                        logBox.CaretIndex = logBox.Text.Length;
                    }
                });
            });
        
        Closing += (object? sender, WindowClosingEventArgs args) => {
            LogBuffer.Complete();
            InMemoryLogger.OnNewMessage -= MessageHandler;
        };
    }
}
