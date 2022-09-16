using Avalonia.Controls;
using System;
using Microsoft.Extensions.DependencyInjection;
using Marccacoin;
using Marccacoin.Shared;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Logging;
using Avalonia.Threading;

namespace holvi_wallet;

public partial class MainWindow : Window
{
    IBlockchainManager BlockchainManager;

    public MainWindow()
    {
        BlockchainManager = Program.ServiceCollection.GetService<IBlockchainManager>() ?? throw new ArgumentNullException(nameof(IBlockchainManager));

        InitializeComponent();
        var model = new MainWindowViewModel();
        DataContext = model;

        Task.Run(async () => {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));

            do {
                var balance = BlockchainManager.GetBalance("FIM0xA101FAF928504BEE3DDBDD9402AE2AD1F5BCF90CC200402FE39C");
                var pending = 0UL;
                var transactions = BlockchainManager.GetTransactions("FIM0xA101FAF928504BEE3DDBDD9402AE2AD1F5BCF90CC200402FE39C", 10);

                await Dispatcher.UIThread.InvokeAsync(() => {
                    model.Balance = balance;
                    model.Pending = pending;
                    model.Transactions = transactions;
                });

            } while(await timer.WaitForNextTickAsync());
        });
    }
}
