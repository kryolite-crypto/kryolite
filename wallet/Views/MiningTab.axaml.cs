using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit;
using Kryolite.EventBus;
using Kryolite.Node;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using Microsoft.Extensions.DependencyInjection;

namespace Kryolite.Wallet;

public partial class MiningTab : UserControl
{
    private bool _init = false;
    private int _threadCount;
    private TextEditor? _logBox;
    private CancellationTokenSource _tokenSource = new ();
    private CancellationTokenSource _stoppingSource = new ();
    private Queue<(DateTime Timestamp, long Hashes)> _snapshots = new (30);
    private readonly MiningTabModel _model = new();
    private readonly System.Timers.Timer _snapshotTimer;

    public MiningTab()
    {
        AvaloniaXamlLoader.Load(this);
        DataContext = _model;

        _logBox = this.FindControl<TextEditor>("LogBox");

        if (_logBox is null)
        {
            throw new Exception("mining tab failed to initialize");
        }

        _logBox.Options.EnableVirtualSpace = false;
        _logBox.Options.AllowScrollBelowDocument = false;
        _logBox.TextArea.TextView.LinkTextForegroundBrush = Brushes.White;
        _logBox.TextArea.TextView.LinkTextUnderline = false;
        _logBox.TextArea.Caret.Offset = _logBox.Text.Length;

        _model.Threads = Environment.ProcessorCount.ToString();

        var eventBus = Program.ServiceCollection.GetRequiredService<IEventBus>();

        eventBus.Subscribe<ChainState>(chainState => {
            _model.CurrentDifficulty = chainState.CurrentDifficulty.ToString();

            if (_model.ActionText != "Start mining")
            {
                WriteLog($"{DateTime.Now}: New job #{chainState?.Id}, diff = {chainState?.CurrentDifficulty}");

                var oldSource = _tokenSource;
                _tokenSource = new();
                oldSource.Cancel();
            }
        });

        AttachedToVisualTree += (_, _) =>
        {
            if (_init)
            {
                return;
            }

            TopLevel.GetTopLevel(this)!.Unloaded += (_, _) =>
            {
                _stoppingSource.Cancel();
                _tokenSource.Cancel();
            };

            _init = true;
        };

        using var scope = Program.ServiceCollection.CreateScope();
        var storeManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();

        var chainState = storeManager.GetChainState();
        _model.CurrentDifficulty = chainState.CurrentDifficulty.ToString();

        _snapshotTimer = new System.Timers.Timer(TimeSpan.FromSeconds(2))
        {
            AutoReset = true
        };

        _snapshotTimer.Elapsed += (sender, e) => {
            _snapshots.Enqueue((DateTime.Now, _hashes));

            var snapshot = _snapshots.Count >= 30 ? _snapshots.Dequeue() : _snapshots.Peek();
            var elapsed = DateTime.Now - snapshot.Timestamp;

            if (elapsed.TotalSeconds == 0)
            {
                return;
            }

            _model.Hashrate = $"{(_hashes - snapshot.Hashes) / elapsed.TotalSeconds:N2} h/s";
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

    public async void WriteLog(string message)
    {
        await Dispatcher.UIThread.InvokeAsync(() => {
            _logBox!.AppendText(message + Environment.NewLine);
        });
    }

    public async void SetMiningState(object? sender, RoutedEventArgs args)
    {
        if (_model.ActionText == "Start mining")
        {
            WriteLog("Starting miner");
            _stoppingSource = new();
            _tokenSource= new();
            StartThreads();
            _model.ActionText = "Stop mining";
            _snapshots.Clear();
            _snapshotTimer.Start();
        }
        else
        {
            WriteLog("Stopping miner");
            _stoppingSource.Cancel();
            _tokenSource.Cancel();
            _model.ActionText = "Start mining";
            _snapshotTimer.Stop();
            
            await Task.Delay(TimeSpan.FromSeconds(1));
            await Dispatcher.UIThread.InvokeAsync(() => {
                _model.Hashrate = "0 h/s";
            });
        }
    }

    private long _hashes;
    private long _blockhashes;

    private void StartThreads()
    {
        if (!int.TryParse(_model.Threads, out _threadCount))
        {
            WriteLog($"Invalid thread count '{_model.Threads}'");
            return;
        }

        for (var i = 0; i < _threadCount; i++)
        {
            var thread = new Thread(() => {
                try
                {
                    using var sha256 = SHA256.Create();

                    var concat = new Concat
                    {
                        Buffer = new byte[64]
                    };

                    var nonce = new Span<byte>(concat.Buffer, 32, 32);
                    var stoppingToken = _stoppingSource.Token;
                    var start = DateTime.Now;

                    _blockhashes = 0;

                    while (!stoppingToken.IsCancellationRequested)
                    {
                        var token = _tokenSource.Token;
                        var blocktemplate = LoadBlocktemplate();
                        var target = blocktemplate.Difficulty.ToTarget();

                        Array.Copy(blocktemplate.Nonce, 0, concat.Buffer, 0, 32);

                        while (!token.IsCancellationRequested)
                        {
                            Random.Shared.NextBytes(nonce);

                            var sha256Hash = Grasshopper.Hash(concat);
                            var result = sha256Hash.ToBigInteger();

                            if (result.CompareTo(target) <= 0)
                            {
                                var timespent = DateTime.Now - start;

                                if (timespent.TotalSeconds > 0)
                                {
                                    WriteLog($"{DateTime.Now}: Block found! {_blockhashes / timespent.TotalSeconds:N2} h/s");
                                }
                                else
                                {
                                    WriteLog($"{DateTime.Now}: Block found!");
                                }

                                blocktemplate.Solution = concat.Buffer[32..];

                                using var scope = Program.ServiceCollection.CreateScope();
                                var storeManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();

                                if (storeManager.AddBlock(blocktemplate, true))
                                {
                                    _model.BlocksFound++;
                                    _model.TotalEarned += blocktemplate.Value;
                                }
                            }

                            Interlocked.Increment(ref _hashes);
                            Interlocked.Increment(ref _blockhashes);

                        }
                    }
                }
                catch (OperationCanceledException)
                {

                }
            });

            thread.UnsafeStart();
        }
    }

    private Blocktemplate LoadBlocktemplate()
    {
            using var scope = Program.ServiceCollection.CreateScope();
            var storeManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();
            var blocktemplate = storeManager.GetBlocktemplate(_model.SelectedWallet!.Address);

            return blocktemplate!;
    }
}
