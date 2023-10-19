using System;
using System.Reactive.Linq;
using System.Threading.Tasks.Dataflow;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit;

namespace Kryolite.Wallet;

public partial class LogViewerDialog : Window
{
    private LogViewerDialogViewModel Model = new ();
    private BufferBlock<string> LogBuffer = new();

    public LogViewerDialog()
    {
        AvaloniaXamlLoader.Load(this);
        DataContext = Model;

        var logBox = this.FindControl<TextEditor>("LogBox");

        if (logBox is null)
        {
            throw new Exception("log viewer failed to initialize");
        }

        logBox.Text = String.Join(Environment.NewLine, InMemoryLogger.Messages.ToArray()) + Environment.NewLine;
        logBox.Options.EnableVirtualSpace = false;
        logBox.Options.AllowScrollBelowDocument = false;
        logBox.TextArea.TextView.LinkTextForegroundBrush = Brushes.White;
        logBox.TextArea.TextView.LinkTextUnderline = false;
        logBox.TextArea.Caret.Offset = logBox.Text.Length;
        logBox.TextArea.Caret.BringCaretToView();

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
                    logBox.AppendText(newText);
                });
            });
        
        Closing += (object? sender, WindowClosingEventArgs args) => {
            LogBuffer.Complete();
            InMemoryLogger.OnNewMessage -= MessageHandler;
        };
    }

    public void ScrollToBottom(object sender, EventArgs args)
    {
        if (sender is not TextEditor logBox)
        {
            return;
        }

        if (!logBox.TextArea.IsFocused)
        {
            logBox.TextArea.Caret.Offset = logBox.Text.Length;
            logBox.TextArea.Caret.BringCaretToView();
        }
    }
}
