﻿using Avalonia;
using System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Kryolite.Node;
using System.IO;
using Microsoft.AspNetCore.Hosting.Server.Features;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Linq;
using Kryolite.Shared;
using MessagePack;
using MessagePack.Resolvers;

namespace Kryolite.Wallet
{
    class Program
    {
        public static IServiceProvider ServiceCollection { get; private set; } = default!;

        private static IWebHost Host = default!;

        [STAThread]
        public static int Main(string[] args) {
            var config = new ConfigurationBuilder()
                .AddCommandLine(args)
                .Build();

            var defaultDataDir = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".kryolite");
            var dataDir = config.GetValue<string>("data-dir", defaultDataDir) ?? defaultDataDir;

            try
            {
                if (!Path.Exists(dataDir))
                {
                    Directory.CreateDirectory(dataDir);
                }

                var versionPath = Path.Join(dataDir, $"store.version.{Constant.STORE_VERSION}");

                if (args.Contains("--resync") || !Path.Exists(versionPath))
                {
                    Console.WriteLine("Performing full resync");
                    var storeDir = Path.Join(dataDir, "store");

                    if (Path.Exists(storeDir))
                    {
                        Directory.Delete(storeDir, true);
                    }
                }

                if (args.Contains("--force-recreate"))
                {
                    var renamedTarget = $"{dataDir}-{DateTimeOffset.Now:yyyyMMddhhmmss}";
                    if (Path.Exists(dataDir))
                    {
                        Directory.Move(dataDir, renamedTarget);
                        Console.WriteLine($"Rename {dataDir} to {renamedTarget}");
                    }
                }

                Directory.CreateDirectory(dataDir);

                var walletRepository = new WalletRepository(config);
                walletRepository.Backup();

                var configPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");

                Host = WebHost.CreateDefaultBuilder()
                    .ConfigureAppConfiguration((hostingContext, config) => config
                        .AddJsonFile(configPath, optional: true, reloadOnChange: true)
                        .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
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

                var logger = Host.Services.GetService<ILogger<Program>>();
                var addresses = Host.ServerFeatures.Get<IServerAddressesFeature>()?.Addresses ?? new List<string>();

                foreach (var address in addresses)
                {
                    logger!.LogInformation($"Now listening on {address}");
                }

                BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            }
            catch (Exception ex)
            {
                var dumpDir = Path.Join(dataDir, "crashdump");
                Directory.CreateDirectory(dumpDir);

                using var dump = File.Create(Path.Join(dumpDir, $"wallet-{DateTime.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)}.txt"));
                dump.Write(Encoding.UTF8.GetBytes(ex.ToString()).AsSpan());

                return 1;
            }

            return 0;
        }

        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure(() => new App())
                .UsePlatformDetect()
                .LogToTrace();
    }
}
