using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit;
using Kryolite.EventBus;
using Kryolite.Interface;
using Kryolite.Node;
using Kryolite.Shared;
using Kryolite.Shared.Algorithm;
using Kryolite.Shared.Dto;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Kryolite.Wallet;

public partial class MiningTab : UserControl
{
    private bool _init = false;
    private int _threadCount;
    private TextEditor? _logBox;
    private CancellationTokenSource _tokenSource = new ();
    private CancellationTokenSource _stoppingSource = new ();
    private Queue<(TimeSpan Timestamp, long Hashes)> _snapshots = new (30);
    private readonly MiningTabModel _model = new();
    private readonly System.Timers.Timer _snapshotTimer;

    public MiningTab()
    {
        AvaloniaXamlLoader.Load(this);
        DataContext = _model;

        _model.Series.Add(new LineSeries<ObservableValue>
        {
            Values = _model.ChartValues,
            Fill = null,
            LineSmoothness = 1,
            GeometrySize = 0
        });

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
        var lifetime = Program.ServiceCollection.GetRequiredService<IHostApplicationLifetime>();

        lifetime.ApplicationStopping.Register(() => {
            _stoppingSource.Cancel();
            _tokenSource.Cancel();
        });

        eventBus.Subscribe<ChainState>(chainState => UpdateStats(chainState));

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
        UpdateStats(chainState);

        _snapshotTimer = new System.Timers.Timer(TimeSpan.FromMilliseconds(500))
        {
            AutoReset = true
        };

        var sw = Stopwatch.StartNew();

        _snapshotTimer.Elapsed += (sender, e) =>
        {
            _snapshots.Enqueue((sw.Elapsed, _hashes));

            var snapshot = _snapshots.Count >= 30 ? _snapshots.Dequeue() : _snapshots.Peek();
            var elapsed = sw.Elapsed - snapshot.Timestamp;

            if (elapsed.TotalSeconds == 0)
            {
                return;
            }

            var hashrate = (_hashes - snapshot.Hashes) / elapsed.TotalSeconds;

            _model.Hashrate = $"{hashrate:N2} h/s";
            // _model.ChartValues.Add(new ObservableValue(hashrate));
        };
    }

    private async void UpdateStats(ChainState chainState)
    {
        if (chainState is null)
        {
            return;
        }

        if (_model.ActionText != "Start mining")
        {
            WriteLog($"{DateTime.Now}: New job #{chainState.Id}, diff = {chainState.CurrentDifficulty}");

            var oldSource = _tokenSource;
            _tokenSource = new();
            oldSource.Cancel();
        }

        using var scope = Program.ServiceCollection.CreateScope();
        var storeManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _model.CurrentDifficulty = chainState.CurrentDifficulty.ToString();
            _model.BlockReward = chainState.BlockReward;
        });
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
                    Span<byte> buf = stackalloc byte[32];
                    Span<byte> concat = stackalloc byte[64];

                    var nonce = concat[32..];
                    var stoppingToken = _stoppingSource.Token;
                    var start = DateTime.Now;

                    _blockhashes = 0;

                    while (!stoppingToken.IsCancellationRequested)
                    {
                        var token = _tokenSource.Token;
                        var blocktemplate = LoadBlocktemplate();
                        var target = blocktemplate.Difficulty.ToTarget();

                        blocktemplate.Nonce.Buffer.CopyTo(concat);

                        while (!token.IsCancellationRequested)
                        {
                            Random.Shared.NextBytes(nonce);

                            Argon2.Hash(concat, buf);

                            var result = new BigInteger(buf, true, true);

                            if (result.CompareTo(target) <= 0)
                            {
                                blocktemplate.Solution = nonce;

                                using var scope = Program.ServiceCollection.CreateScope();
                                var storeManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();

                                var status = "REJECTED";

                                if (storeManager.AddBlock(blocktemplate, true))
                                {
                                    _model.BlocksFound++;
                                    status = "SUCCESS";
                                }

                                var timespent = DateTime.Now - start;

                                if (timespent.TotalSeconds > 0)
                                {
                                    WriteLog($"{DateTime.Now}: [{status}] Block found! {_blockhashes / timespent.TotalSeconds:N2} h/s");
                                }
                                else
                                {
                                    WriteLog($"{DateTime.Now}: [{status}] Block found!");
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

            thread.Start();
        }

        WriteLog("Mining started");
    }

    private BlockTemplate LoadBlocktemplate()
    {
            using var scope = Program.ServiceCollection.CreateScope();
            var storeManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();
            var blocktemplate = storeManager.GetBlocktemplate(_model.SelectedWallet!.Address);

            return blocktemplate!;
    }
}
