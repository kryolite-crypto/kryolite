using System.Threading.Tasks.Dataflow;
using Kryolite.Shared;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Kryolite.Node;

public class POSService : BackgroundService
{
    private readonly IWalletManager walletManager;
    private readonly IBlockchainManager blockchainManager;
    private readonly ILogger<POSService> logger;

    public POSService(IWalletManager walletManager, IBlockchainManager blockchainManager, ILogger<POSService> logger)
    {
        this.walletManager = walletManager ?? throw new ArgumentNullException(nameof(walletManager));
        this.blockchainManager = blockchainManager ?? throw new ArgumentNullException(nameof(blockchainManager));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var nodeWallet = walletManager.GetNodeWallet();

        if (nodeWallet == null) {
            nodeWallet = walletManager.CreateWallet(WalletType.NODE);
        }

        /*blockchainManager.OnWalletUpdated(new ActionBlock<Wallet>(wallet => {
            if (!enabled && wallet.Balance >= collateral) {
                PromoteNode();
            }

            if (!enabled && wallet.Balance < collateral) {
                DemoteNode();
            }
        }));
        
        if (nodeWallet.Balance >= collateral) {
            PromoteNode();
            return;
        }*/

        DemoteNode();
        await Task.CompletedTask;
    }

    private void PromoteNode()
    {
        logger.LogInformation("POS           [ACTIVE]");
    }

    private void DemoteNode()
    {
        logger.LogInformation("POS           [INACTIVE]");
    }
}
