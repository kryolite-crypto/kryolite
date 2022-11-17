using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

namespace Marccacoin.Daemon;

internal class Program
{
    static async Task Main(string[] args)
    {
        Console.ForegroundColor = ConsoleColor.DarkYellow;
        Console.WriteLine(@"                                         
 __  __    _    ____   ____ ____    _    
|  \/  |  / \  |  _ \ / ___/ ___|  / \   
| |\/| | / _ \ | |_) | |  | |     / _ \  
| |  | |/ ___ \|  _ <| |__| |___ / ___ \ 
|_|  |_/_/   \_|_| \_\\____\____/_/   \_\                                  
        _   _  ___  ____  _____          
       | \ | |/ _ \|  _ \| ____|         
       |  \| | | | | | | |  _|           
       | |\  | |_| | |_| | |___          
       |_| \_|\___/|____/|_____|         
                                         ");
        Console.ForegroundColor = ConsoleColor.Gray;

         await WebHost.CreateDefaultBuilder()
            .ConfigureAppConfiguration(c => c
                .AddJsonFile($"appsettings.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables(prefix: "MARKKA__")
                .AddCommandLine(args))
            .ConfigureLogging(configure => configure.AddConsoleFormatter<CleanConsoleFormatter, ConsoleFormatterOptions>())
            .UseStartup<Startup>()
            .Build()
            .RunAsync();
    }
}