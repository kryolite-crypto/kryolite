using Avalonia.Controls;
using System;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks;
using Avalonia.Threading;
using System.Collections.ObjectModel;
using System.Linq;
using System.Collections.Generic;
using Avalonia.Markup.Xaml;
using Kryolite.Node;
using Kryolite.Shared;
using System.Collections.Concurrent;
using Kryolite.EventBus;
using Avalonia;
using System.IO;
using Microsoft.Extensions.Configuration;
using System.Text.Json;
using Kryolite.Shared.Blockchain;
using Kryolite.Shared.Dto;
using System.Web;
using Kryolite.Node.Network;
using Avalonia.Interactivity;
using Microsoft.Extensions.Hosting;
using Kryolite.ByteSerializer;

namespace Kryolite.Wallet;

public partial class MainWindow : Window
{
    private IWalletManager _walletManager;
    private IStoreManager _storeManager;
    private IEventBus _eventBus;
    private IConnectionManager _connMan;

    private FileSystemWatcher _watcher = new();
    private MainWindowViewModel _model = new MainWindowViewModel();
    public ConcurrentDictionary<Address, Account> Accounts;

    public MainWindow()
    {
        var scope = Program.ServiceCollection.CreateScope();
        _walletManager = scope.ServiceProvider.GetService<IWalletManager>() ?? throw new ArgumentNullException(nameof(IWalletManager));
        _storeManager = scope.ServiceProvider.GetService<IStoreManager>() ?? throw new ArgumentNullException(nameof(IStoreManager));
        _eventBus = Program.ServiceCollection.GetService<IEventBus>() ?? throw new ArgumentNullException(nameof(IEventBus));
        _connMan = scope.ServiceProvider.GetRequiredService<IConnectionManager>();

        Accounts = new(_walletManager.GetAccounts());
        DataContext = _model;

        AvaloniaXamlLoader.Load(this);

#if DEBUG
        this.AttachDevTools();
#endif
        Opened += (object? sender, EventArgs args) =>
        {
            if (_walletManager.WalletExists())
            {
                Initialize();
            }
            else
            {
                FirstTimeExperience();
            }
        };

        _model.ViewLogClicked += (object? sender, EventArgs args) =>
        {
            var dialog = new LogViewerDialog();
            dialog.Show(this);
        };

        _model.AboutClicked += (object? sender, EventArgs args) =>
        {
            var dialog = new AboutDialog();
            dialog.Show(this);
        };

        _connMan.NodeConnected += async (object? sender, NodeConnection connection) =>
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _model.ConnectedPeers = _connMan.GetConnectedNodes().Count();
            });
        };

        _connMan.NodeDisconnected += async (object? sender, NodeConnection connection) =>
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _model.ConnectedPeers = _connMan.GetConnectedNodes().Count();
            });
        };

        _eventBus.Subscribe<ChainState>(async state =>
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _model.Blocks = state.Id;
            });
        });

        _eventBus.Subscribe<Ledger>(async ledger =>
        {
            if (!Accounts.ContainsKey(ledger.Address))
            {
                return;
            }

            var transactions = _storeManager.GetLastNTransctions(ledger.Address, 5);
            var txs = transactions.Select(x =>
            {
                var isRecipient = Accounts.ContainsKey(x.To!);

                var tm = new TransactionModel
                {
                    Recipient = x.To!,
                    Amount = isRecipient ? (long)x.Value : -(long)x.Value,
                    Timestamp = x.Timestamp
                };

                return tm;
            }).ToList();

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _model.State.UpdateWallet(ledger, txs);
            });
        });
    }

    private void Initialize()
    {
        Task.Run(async () =>
        {
            try
            {
                Program.App.Start();

                var toAdd = new List<AccountModel>();
                var balance = 0UL;
                var pending = 0UL;

                foreach (var account in Accounts.Values)
                {
                    var ledger = _storeManager.GetLedger(account.Address);
                    var txs = _storeManager.GetLastNTransctions(account.Address, 5);

                    var wm = new AccountModel
                    {
                        Description = account.Description,
                        Address = account.Address.ToString(),
                        PublicKey = account.PublicKey,
                        Balance = ledger?.Balance ?? 0,
                        Pending = ledger?.Pending ?? 0,
                        Transactions = txs.Select(x =>
                        {
                            var isSender = x.From == account.Address;

                            var tm = new TransactionModel
                            {
                                Recipient = isSender ? x.From! : x.To!,
                                Amount = isSender ? -(long)x.Value : (long)x.Value,
                                Timestamp = x.Timestamp
                            };

                            return tm;
                        }).ToList()
                    };

                    balance += ledger?.Balance ?? 0;
                    pending += ledger?.Pending ?? 0;

                    toAdd.Add(wm);
                }

                var transactions = toAdd
                    .SelectMany(wallet => wallet.Transactions)
                    .OrderByDescending(tx => tx.Timestamp)
                    .Take(5)
                    .ToList();

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    _model.State.Accounts = new ObservableCollection<AccountModel>(toAdd);
                    _model.State.Balance = (long)balance;
                    _model.State.Pending = (long)pending;
                    _model.State.Transactions = transactions;
                });

                await ConsumePipe();
                StartPipeWatcher();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
        });

        Task.Run(async () =>
        {
            using var scope = Program.ServiceCollection.CreateScope();
            var blockchainManager = scope.ServiceProvider.GetService<IStoreManager>() ?? throw new ArgumentNullException(nameof(IStoreManager));

            var state = blockchainManager.GetChainState();

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _model.Blocks = state.Id;
            });
        });

        _model.ConnectedPeers = _connMan.GetConnectedNodes().Count();
    }

    private void StartPipeWatcher()
    {
        using var scope = Program.ServiceCollection.CreateScope();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var defaultDataDir = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".kryolite");
        var dataDir = config.GetValue<string>("data-dir", defaultDataDir) ?? defaultDataDir;

        _watcher.Path = dataDir;
        _watcher.Filter = ".pipe";
        _watcher.NotifyFilter = NotifyFilters.LastWrite;

        _watcher.Changed += async (sender, args) =>
        {
            _watcher.EnableRaisingEvents = false;
            await ConsumePipe();
            _watcher.EnableRaisingEvents = true;
        };

        _watcher.EnableRaisingEvents = true;
    }

    private async Task ConsumePipe()
    {
        // Lets see if there are any pending requests
        using var scope = Program.ServiceCollection.CreateScope();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var defaultDataDir = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".kryolite");
        var dataDir = config.GetValue<string>("data-dir", defaultDataDir) ?? defaultDataDir;
        var pipePath = Path.Join(dataDir, ".pipe");

        var messages = await File.ReadAllLinesAsync(pipePath);

        if (messages is null)
        {
            return;
        }

        foreach (var message in messages)
        {
            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            await HandleMessage(message);
        }

        File.WriteAllText(pipePath, string.Empty);
    }

    private async Task HandleMessage(string message)
    {
        try
        {
            var parts = message.Split("/");

            if (parts.Length != 4 || parts[0] != "kryolite:call")
            {
                Console.WriteLine($"Invalid message '{message}'");
                return;
            }

            var contractAddress = (Address)parts[1];
            var methodCall = JsonSerializer.Deserialize(HttpUtility.UrlDecode(parts[3]), SharedSourceGenerationContext.Default.CallMethod);

            var contract = _storeManager.GetContract(contractAddress);

            if (contract is null)
            {
                Console.WriteLine($"Contract not found '{contractAddress}'");
                return;
            }

            if (!ulong.TryParse(parts[2], out var amount))
            {
                Console.WriteLine($"Invalid amount'{parts[2]}'");
                return;
            }

            if (methodCall is null)
            {
                Console.WriteLine($"Invalid method params '{message}'");
                return;
            }

            var method = contract.Manifest.Methods.Where(x => x.Name == methodCall.Method).FirstOrDefault();

            if (method is null)
            {
                Console.WriteLine($"Method not found '{methodCall.Method}'");
                return;
            }

            var methodParams = new List<ParamModel>();

            for (var i = 0; i < (methodCall?.Params?.Length ?? 0); i++)
            {
                methodParams.Add(new ParamModel
                {
                    Name = method.Params[i].Description ?? method.Params[i].Name,
                    Value = methodCall?.Params?[i] ?? "n/a"
                });
            }

            var account = await AuthorizePaymentDialog.Show(
                method.Description ?? method.Name,
                methodParams,
                amount,
                contract,
                _model.State.Accounts,
                this
            );

            if (account is null)
            {
                return;
            }

            var payload = new TransactionPayload
            {
                Payload = methodCall
            };

            var transaction = new Transaction
            {
                TransactionType = TransactionType.PAYMENT,
                PublicKey = account.PublicKey,
                To = contract.Address,
                Value = amount,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Data = Serializer.Serialize<TransactionPayload>(payload)
            };

            transaction.Sign(_walletManager.GetPrivateKey(account.PublicKey));

            var result = _storeManager.AddTransaction(new TransactionDto(transaction), true);

            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                await ConfirmDialog.Show($"Transaction Status: {result}", true, this);
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
        }
    }

    private void FirstTimeExperience()
    {
        _model.FirstTimeExperience = true;
        _model.WelcomePage = true;
    }

    public void CreateNewWallet(object? sender, RoutedEventArgs args)
    {
        _model.WelcomePage = false;
        _model.NewSeedPage = true;
        _model.Mnemonic = Mnemonic.CreateMnemonic();
    }


    public void ImportWalletFromSeed(object? sender, RoutedEventArgs args)
    {
        _model.WelcomePage = false;
        _model.ImportSeedPage = true;
    }

    public void CreateOrImport(object? sender, RoutedEventArgs args)
    {
        _model.WelcomePage = false;
        _model.NewSeedPage = false;
        _model.ImportSeedPage = false;
        _model.FirstTimeExperience = false;

        if (!Mnemonic.TryConvertMnemonicToSeed(_model.Mnemonic, out var seed))
        {
            _model.Error = "Invalid mnemonic seed";
        }

        if (seed.Length != 32)
        {
            _model.Error = "Invalid seed length";
        }

        _walletManager.CreateWalletFromSeed(seed);

        _model.Error = string.Empty;

        Initialize();
    }

    public void Back(object? sender, RoutedEventArgs args)
    {
        _model.NewSeedPage = false;
        _model.ImportSeedPage = false;
        _model.WelcomePage = true;
    }
}
