﻿using Avalonia;
using System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Kryolite.Node;
using System.IO;
using System.Globalization;
using System.Text;
using System.Linq;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Microsoft.AspNetCore.Builder;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

namespace Kryolite.Wallet
{
    class Program
    {
        public static WebApplication App { get; private set; } = default!;
        public static IServiceProvider ServiceCollection { get; private set; } = default!;

        [STAThread]
        public static int Main(string[] args)
        {
            var config = new ConfigurationBuilder()
                .AddCommandLine(args)
                .Build();

            DataDirectory.EnsureExists(config, args, out var dataDir, out _);
            var pipePath = Path.Join(dataDir, $".pipe");

            try
            {
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

                CreateUriScheme(dataDir);

                var builder = WebApplication.CreateSlimBuilder(args)
                    .BuildKryoliteNode(args);

                builder.Logging.AddProvider(new InMemoryLoggerProvider());

                var app = builder.Build();

                app.UseNodeMiddleware();

                App = app;
                ServiceCollection = app.Services;

                if (!Path.Exists(pipePath))
                {
                    File.WriteAllText(pipePath, string.Empty);
                }

                var ava = BuildAvaloniaApp();

                var lifetime = new ClassicDesktopStyleApplicationLifetime
                {
                    Args = args,
                    ShutdownMode = ShutdownMode.OnMainWindowClose
                };

                ava.SetupWithLifetime(lifetime);

                Console.CancelKeyPress += (s, e) =>
                {
                    Dispatcher.UIThread.InvokeAsync(() => lifetime.Shutdown(0));
                    app.Lifetime.StopApplication();
                    e.Cancel = true;
                };

                lifetime.Start(args);

                File.Delete(pipePath);
                return 0;
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
        }

        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();

        private static void CreateUriScheme(string dataDir)
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
                        Exec="{exePath}" --data-dir "{dataDir}" %u
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
                    cmd?.SetValue("", $"\"{exePath}\" --data-dir \"{dataDir}\" %1");
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
