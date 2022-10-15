using Avalonia;
using System;
using Microsoft.Extensions.DependencyInjection;
using Marccacoin;
using Marccacoin.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using System.Threading;

namespace holvi_wallet
{
    class Program
    {
        public static readonly IServiceProvider ServiceCollection;

        private static IWebHost Host;

        static Program()
        {
            var configuration = new ConfigurationBuilder()
                .AddJsonFile($"appsettings.json", optional: true, reloadOnChange: true)
                .Build();

            Host = WebHost.CreateDefaultBuilder()
                .ConfigureLogging(logging => logging.AddProvider(new InMemoryLoggerProvider()))
                .UseStartup<Startup>()
                .Build();

            ServiceCollection = Host.Services;

            Host.Start();
        }

        [STAThread]
        public static void Main(string[] args) {
            BuildAvaloniaAppWithServices().StartWithClassicDesktopLifetime(args);
        }

        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaAppWithServices()
            => AppBuilder.Configure(() => new App())
                .UsePlatformDetect()
                .LogToTrace();
    }
}
