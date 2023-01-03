using Kryolite.Node;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

namespace Kryolite.Daemon;

internal class Program
{
    static async Task Main(string[] args)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(@"
 __                         .__  .__  __
|  | _________ ___.__. ____ |  | |__|/  |_  ____
|  |/ /\_  __ <   |  |/  _ \|  | |  \   __\/ __ \
|    <  |  | \/\___  (  <_> )  |_|  ||  | \  ___/
|__|_ \ |__|   / ____|\____/|____/__||__|  \___  >
     \/        \/                              \/
                            node
                                         ");
        Console.ForegroundColor = ConsoleColor.Gray;

         await WebHost.CreateDefaultBuilder()
            .ConfigureAppConfiguration((hostingContext, config) => config
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddJsonFile($"appsettings.{hostingContext.HostingEnvironment.EnvironmentName}.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables(prefix: "KRYOLITE__")
                .AddCommandLine(args))
            .ConfigureLogging(configure => configure.AddConsoleFormatter<CleanConsoleFormatter, ConsoleFormatterOptions>())
            .UseStartup<Startup>()
            .Build()
            .RunAsync();
    }
}