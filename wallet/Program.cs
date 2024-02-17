using Avalonia;
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
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Kryolite.Wallet
{
    class Program
    {
        public static IServiceProvider ServiceCollection { get; private set; } = default!;

        private static IWebHost Host = default!;

        [STAThread]
        public static int Main(string[] args)
        {
            Startup.RegisterFormatters();

            var config = new ConfigurationBuilder()
                .AddCommandLine(args)
                .Build();

            var defaultDataDir = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".kryolite");
            var dataDir = config.GetValue<string>("data-dir", defaultDataDir) ?? defaultDataDir;
            var pipePath = Path.Join(dataDir, $".pipe");

            try
            {
                CreateUriScheme();

                Directory.CreateDirectory(dataDir);

                foreach (var arg in args)
                {
                    if (arg.StartsWith("kryolite:"))
                    {
                        var isRunning = File.Exists(pipePath);

                        File.WriteAllText(pipePath, arg);

                        if (isRunning)
                        {
                            return 0;
                        }

                        break;
                    }
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

                    if (File.Exists(versionPath))
                    {
                        File.Delete(versionPath);
                    }
                }

                var configVersion = Path.Join(dataDir, $"config.version.{Constant.STORE_VERSION}");

                if (args.Contains("--force-recreate") || !Path.Exists(configVersion))
                {
                    var renamedTarget = $"{dataDir}-{DateTimeOffset.Now:yyyyMMddhhmmss}";
                    if (Path.Exists(dataDir))
                    {
                        Directory.Move(dataDir, renamedTarget);
                        Console.WriteLine($"Rename {dataDir} to {renamedTarget}");
                    }
                }

                Directory.CreateDirectory(dataDir);

                if (!Path.Exists(configVersion))
                {
                    File.WriteAllText(configVersion, Constant.CONFIG_VERSION);
                }

                if (!Path.Exists(pipePath))
                {
                    File.WriteAllText(pipePath, string.Empty);
                }

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

                var logger = Host.Services.GetService<ILogger<Program>>();
                var addresses = Host.ServerFeatures.Get<IServerAddressesFeature>()?.Addresses ?? new List<string>();

                foreach (var address in addresses)
                {
                    logger!.LogInformation($"Now listening on {address}");
                }

                BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

                File.Delete(pipePath);
            }
            catch (Exception ex)
            {
                var dumpDir = Path.Join(dataDir, "crashdump");
                Directory.CreateDirectory(dumpDir);

                using var dump = File.Create(Path.Join(dumpDir, $"wallet-{DateTime.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)}.txt"));
                dump.Write(Encoding.UTF8.GetBytes(ex.ToString()).AsSpan());

                Console.WriteLine(ex.ToString());

                File.Delete(pipePath);

                return 1;
            }

            return 0;
        }

        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();

        private static void CreateUriScheme()
        {
            try
            {
                var exePath = Process.GetCurrentProcess().MainModule?.FileName;

                if (string.IsNullOrEmpty(exePath))
                {
                    return;
                }

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    var definitionFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local/share/applications/kryolite.desktop");
                    var definition = $"""
                        [Desktop Entry]
                        Name=Kryolite
                        Exec="{exePath}" %u
                        Type=Application
                        Terminal=false
                        MimeType=x-scheme-handler/kryolite;
                        """;

                    File.WriteAllText(definitionFile, definition);

                    Process.Start("xdg-mime", "default kryolite.desktop x-scheme-handler/kryolite");
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    using var software = Registry.CurrentUser.OpenSubKey("Software", true);
                    using var classes = software?.OpenSubKey("Classes", true) ?? throw new Exception("failed to open registry");

                    if (classes.GetSubKeyNames().Contains("kryolite"))
                    {
                        classes?.DeleteSubKeyTree("kryolite");
                    }

                    using var key = classes?.CreateSubKey("kryolite");
                    using var cmd = key?.CreateSubKey(@"shell\open\command");

                    key?.SetValue("URL Protocol", "kryolite");
                    cmd?.SetValue("", $"\"{exePath}\" %1");
                }

                // MacOS registration is done by plist file
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Unable to register kryolite url scheme: {ex.Message}");
            }
        }
    }
}
