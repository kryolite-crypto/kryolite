using holviwallet;
using holviwallet.Data;
using BlazorDesktop.Hosting;
using Microsoft.AspNetCore.Components.Web;
using Marccacoin;

var builder = BlazorDesktopHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");
builder.Window.UseTitle("Holvi Wallet");

builder.Services.AddSingleton<IBlockchainRepository, BlockchainRepository>()
                .AddSingleton<IBlockchainManager, BlockchainManager>()
                .AddSingleton<IDiscoveryManager, DiscoveryManager>()
                .AddHostedService<DiscoveryService>()
                .AddHostedService<BlockchainService>()
                .AddHostedService<MempoolService>()
                .AddHostedService<SampoService>();

if (builder.HostEnvironment.IsDevelopment())
{
    builder.UseDeveloperTools();
}

await builder.Build().RunAsync();
