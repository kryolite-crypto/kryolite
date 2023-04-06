using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Kryolite.Node;

public class WalletService : BackgroundService
{
    public static event EventHandler<ApprovalEventArgs>? ApprovalEvent;
    public static event EventHandler<TransferTokenEventArgs>? TransferTokenEvent;
    public static event EventHandler<ConsumeTokenEventArgs>? ConsumeTokenEvent;
    public static event EventHandler<GenericEventArgs>? GenericEvent;

    private readonly IWalletManager walletManager;
    private readonly StartupSequence startup;
    private readonly ILogger<BlockchainService> logger;
    private readonly IConfiguration configuration;

    public WalletService(IWalletManager walletManager, StartupSequence startup, ILogger<BlockchainService> logger, IConfiguration configuration)
    {
        this.walletManager = walletManager ?? throw new ArgumentNullException(nameof(walletManager));
        this.startup = startup ?? throw new ArgumentNullException(nameof(startup));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TransferTokenEvent += TransferTokenEventHandler;
        ConsumeTokenEvent += ConsumeTokenEventHandler;

        logger.LogInformation("Wallet       \x1B[1m\x1B[32m[UP]\x1B[39m\x1B[22m");

        await Task.CompletedTask;
    }

    private void TransferTokenEventHandler(object? sender, TransferTokenEventArgs e)
    {

    }

    private void ConsumeTokenEventHandler(object? sender, ConsumeTokenEventArgs e)
    {
        
    }

    public static void RaiseApprovalEvent(object? sender, ApprovalEventArgs args)
        => ApprovalEvent?.Invoke(sender, args);

    public static void RaiseTransferTokenEvent(object? sender, TransferTokenEventArgs args)
        => TransferTokenEvent?.Invoke(sender, args);

    public static void RaiseConsumeTokenEvent(object? sender, ConsumeTokenEventArgs args)
        => ConsumeTokenEvent?.Invoke(sender, args);

    public static void RaiseGenericEvent(object? sender, GenericEventArgs args)
        => GenericEvent?.Invoke(sender, args);
}
