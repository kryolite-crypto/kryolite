using holviwallet;
using holviwallet.Data;
using BlazorDesktop.Hosting;
using Microsoft.AspNetCore.Components.Web;

var builder = BlazorDesktopHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");
builder.Window.UseTitle("Holvi Wallet");

builder.Services.AddSingleton<WeatherForecastService>();

if (builder.HostEnvironment.IsDevelopment())
{
    builder.UseDeveloperTools();
}

await builder.Build().RunAsync();
