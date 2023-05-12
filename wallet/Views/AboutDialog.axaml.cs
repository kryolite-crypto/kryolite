using Avalonia.Controls;
using Kryolite.Node;
using Kryolite.Shared;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace Kryolite.Wallet;

public partial class AboutDialog : Window
{
    private AboutDialogViewModel Model = new();

    public AboutDialog()
    {
        InitializeComponent();
        DataContext = Model;

        var configuration = Program.ServiceCollection.GetService<IConfiguration>() ?? throw new ArgumentNullException(nameof(IConfiguration));

        var attr = Attribute.GetCustomAttribute(Assembly.GetEntryAssembly(), typeof(AssemblyInformationalVersionAttribute))
            as AssemblyInformationalVersionAttribute;

        Model.NetworkName = configuration.GetValue<string>("NetworkName") ?? string.Empty;
        Model.Version = $"Version {attr?.InformationalVersion}";

        if (Version.TryParse(attr?.InformationalVersion, out var version))
        {
            _ = CheckVersion(version);
        }
    }

    private async Task CheckVersion(Version currentVersion)
    {
        using var httpClient = new HttpClient();

        httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("kryolite", currentVersion.ToString()));

        var result = await httpClient.GetAsync("https://api.github.com/repos/kryolite-crypto/kryolite/releases/latest");

        if (!result.IsSuccessStatusCode)
        {
            return;
        }

        var releaseStr = await result.Content.ReadAsStringAsync();
        var release = JsonSerializer.Deserialize<GithubRelease>(releaseStr);

        if (Version.TryParse(release?.TagName, out var latestVersion))
        {
            Model.UpdateAvailable = latestVersion > currentVersion;
        }
    }
}
