﻿using Avalonia;
using System;
using Microsoft.Extensions.DependencyInjection;
using Marccacoin;
using Marccacoin.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using System.Threading;
using System.Diagnostics;
using System.Linq;

namespace holvi_wallet
{
    class Program
    {
        public static IServiceProvider ServiceCollection { get; private set; }

        private static IWebHost Host;

        [STAThread]
        public static void Main(string[] args) {
            var configuration = new ConfigurationBuilder()
                .AddCommandLine(args)
                .AddJsonFile($"appsettings.json", optional: true, reloadOnChange: true)
                .Build();

            Host = WebHost.CreateDefaultBuilder()
                .UseConfiguration(configuration)
                .ConfigureLogging(logging => logging.AddProvider(new InMemoryLoggerProvider()))
                .UseStartup<Startup>()
                .Build();

            ServiceCollection = Host.Services;

            Host.Start();

            BuildAvaloniaAppWithServices().StartWithClassicDesktopLifetime(args);
        }

        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaAppWithServices()
            => AppBuilder.Configure(() => new App())
                .UsePlatformDetect()
                .LogToTrace();
    }
}
