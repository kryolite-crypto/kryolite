using Kryolite.Node;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kryolite.Daemon;

public class Startup
{
    private IConfiguration Configuration { get; }

    public Startup(IConfiguration configuration)
    {
        Configuration = configuration;
    }

    public void Configure(IApplicationBuilder app)
    {
        app.UseRouting();
        app.UseEndpoints(endpoints =>
        {
            endpoints.MapControllers();
        });
    }

    public void ConfigureServices(IServiceCollection services)
    {
        var dataDir = Configuration.GetValue<string>("data-dir") ?? "data";
        Directory.CreateDirectory(dataDir);

        BlockchainService.DATA_PATH = dataDir;

        services.AddSingleton<IBlockchainManager, BlockchainManager>()
                .AddSingleton<Lazy<IBlockchainManager>>(c => new Lazy<IBlockchainManager>(c.GetService<IBlockchainManager>()!))
                .AddSingleton<INetworkManager, NetworkManager>()
                .AddSingleton<IMempoolManager, MempoolManager>()
                .AddSingleton<IWalletManager, WalletManager>()
                .AddHostedService<NetworkService>()
                .AddHostedService<BlockchainService>()
                .AddHostedService<MempoolService>()
                .AddHostedService<SampoService>()
                .AddSingleton<StartupSequence>()
                .AddRouting()
                .AddControllers()
                .AddNewtonsoftJson();
    }
}