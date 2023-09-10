using Kryolite.Node;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using System.Net.Sockets;
using Microsoft.AspNetCore.Hosting.Server.Features;
using System.Reflection;

namespace Kryolite.Daemon;

internal class Program
{
    static async Task Main(string[] args)
    {
        var attr = Attribute.GetCustomAttribute(Assembly.GetEntryAssembly()!, typeof(AssemblyInformationalVersionAttribute)) 
            as AssemblyInformationalVersionAttribute;

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($@"
 __                         .__  .__  __
|  | _________ ___.__. ____ |  | |__|/  |_  ____
|  |/ /\_  __ <   |  |/  _ \|  | |  \   __\/ __ \
|    <  |  | \/\___  (  <_> )  |_|  ||  | \  ___/
|__|_ \ |__|   / ____|\____/|____/__||__|  \___  >
     \/        \/                              \/
                            {attr?.InformationalVersion}
                                         ");
        Console.ForegroundColor = ConsoleColor.Gray;

        var config = new ConfigurationBuilder()
            .AddCommandLine(args)
            .Build();

        var defaultDataDir = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".kryolite");
        var dataDir = config.GetValue<string>("data-dir", defaultDataDir) ?? defaultDataDir;

        Directory.CreateDirectory(dataDir);

        if (args.Contains("--resync"))
        {
            Console.WriteLine("Performing full resync");
            var storeDir = Path.Join(dataDir, "store");

            Directory.Delete(storeDir, true);
        }

        var configPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        var app = WebHost.CreateDefaultBuilder()
            .ConfigureAppConfiguration((hostingContext, config) => config
                .AddJsonFile(configPath, optional: true, reloadOnChange: true)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddJsonFile($"appsettings.{hostingContext.HostingEnvironment.EnvironmentName}.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables(prefix: "KRYOLITE__")
                .AddCommandLine(args))
            .ConfigureLogging(configure => configure.AddConsoleFormatter<CleanConsoleFormatter, ConsoleFormatterOptions>())
            .UseStartup<Startup>()
            .Build();

        await app.StartAsync();

        var logger = app.Services.GetService<ILogger<Program>>();
        var addresses = app.ServerFeatures.Get<IServerAddressesFeature>()?.Addresses ?? new List<string>();

        foreach (var address in addresses)
        {
            logger!.LogInformation($"Now listening on {address}");
        }

        app.Services.GetService<StartupSequence>()!
            .Application.Set();

        await app.WaitForShutdownAsync();
    }
}
