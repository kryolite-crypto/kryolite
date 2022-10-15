using System;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace holvi_wallet;

public partial class LogViewerDialog : Window
{
    private LogViewerDialogViewModel Model = new ();
    private BufferBlock<string> LogBuffer = new();
    private CancellationTokenSource TokenSource = new();

    public LogViewerDialog()
    {
        AvaloniaXamlLoader.Load(this);
        DataContext = Model;

        var logBox = this.FindControl<TextBox>("LogBox");

        logBox.Text = String.Join(Environment.NewLine, InMemoryLogger.Messages.ToArray()) + Environment.NewLine;

        EventHandler<string> MessageHandler = delegate(object? sender, string message)
        {
            LogBuffer.Post(message);
        };

        InMemoryLogger.OnNewMessage += MessageHandler;

        Task.Run(async () => {
            try
            {
                var token = TokenSource.Token;

                while(!TokenSource.IsCancellationRequested) {
                    await LogBuffer.OutputAvailableAsync(token);

                    if(LogBuffer.TryReceiveAll(out var newMessages))
                    {
                        await Dispatcher.UIThread.InvokeAsync(() => {
                            logBox.Text += String.Join(Environment.NewLine, newMessages) + Environment.NewLine;
                            if (!logBox.IsFocused)
                            {
                                logBox.CaretIndex = logBox.Text.Length;
                            }
                        });
                    }

                    await Task.Delay(100, token);
                }
            } 
            catch (TaskCanceledException)
            {

            }
            catch (Exception ex)
            {
                throw new Exception("Message Update task failed", ex);
            }
        });
        
        Closing += (object? sender, CancelEventArgs args) => {
            TokenSource.Cancel();
            InMemoryLogger.OnNewMessage -= MessageHandler;
        };
    }
}
