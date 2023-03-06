using Avalonia;
using System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Kryolite.Node;
using System.IO;

namespace Kryolite.Wallet
{
    class Program
    {
        public static IServiceProvider ServiceCollection { get; private set; }

        private static IWebHost Host;

        [STAThread]
        public static void Main(string[] args) {
            var dataDir = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".kryolite");
            Directory.CreateDirectory(dataDir);

            using var fileStream = new FileStream(Path.Join(dataDir, ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

            Host = WebHost.CreateDefaultBuilder()
                .ConfigureAppConfiguration((hostingContext, config) => config
                    .AddJsonFile($"appsettings.json", optional: true, reloadOnChange: true)
                    .AddJsonFile($"appsettings.{hostingContext.HostingEnvironment.EnvironmentName}.json", optional: true, reloadOnChange: true)
                    .AddEnvironmentVariables(prefix: "KRYOLITE__")
                    .AddCommandLine(args))
                .ConfigureLogging(logging => logging.AddProvider(new InMemoryLoggerProvider()))
                .UseStartup<Startup>()
                .Build();

            ServiceCollection = Host.Services;

            Host.Start();

            ServiceCollection.GetService<StartupSequence>()?
                .Application.Set();

            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }

        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure(() => new App())
                .UsePlatformDetect()
                .LogToTrace();
    }
}
