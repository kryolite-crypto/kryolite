using Microsoft.AspNetCore.Builder;
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
        app.UseRouting();
        app.UseEndpoints(endpoints =>
        {
            endpoints.MapControllers();
        });
    }

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IBlockchainRepository, BlockchainRepository>()
                .AddSingleton<IBlockchainManager, BlockchainManager>()
                .AddSingleton<IDiscoveryManager, DiscoveryManager>()
                .AddHostedService<DiscoveryService>()
                .AddHostedService<BlockchainService>()
                .AddHostedService<MempoolService>()
                .AddHostedService<SampoService>()
                .AddRouting()
                .AddControllers()
                .AddNewtonsoftJson();
    }
}