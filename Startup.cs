using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Marccacoin;

public class Startup
{
    private IConfiguration Configuration { get; }

    public Startup(IConfiguration configuration)
    {
        Configuration = configuration;
    }

    public void Configure(IApplicationBuilder app)
    {

    }

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IBlockchainRepository, BlockchainRepository>()
                .AddSingleton<IBlockchainManager, BlockchainManager>()
                .AddSingleton<IDiscoveryManager, DiscoveryManager>()
                .AddHostedService<DiscoveryService>()
                .AddHostedService<BlockchainService>()
                .AddHostedService<MempoolService>()
                .AddHostedService<SampoService>();
    }
}