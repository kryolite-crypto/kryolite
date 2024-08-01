using System.CommandLine;
using System.CommandLine.Binding;
using Kryolite.Node.Repository;
using Microsoft.Extensions.Configuration;

namespace Kryolite.Cli;

public static class NodeCmd
{
    public static Command Build(IConfiguration configuration)
    {
        var nodeCmd = new Command("node", "Manage node");

        nodeCmd.AddCommand(NodeIdentityCmd.Build(configuration));

        return nodeCmd;
    }
}

public static class NodeIdentityCmd
{
    // TODO: Make DI work for this
    private static IConfiguration _configuration;

    public static Command Build(IConfiguration configuration)
    {
        _configuration = configuration;

        var keysCmd = new Command("keys", "Manage node keys");

        var showCmd = new Command("show", "Show node keys");
        var importCmd = new Command("import", "Import node keys");
        var exportCmd = new Command("export", "Export node keys");
        var recreateCmd = new Command("recreate", "Recreate node keys");

        keysCmd.AddCommand(showCmd);
        keysCmd.AddCommand(importCmd);
        keysCmd.AddCommand(exportCmd);
        keysCmd.AddCommand(recreateCmd);

        var outputOpt = new Option<string>("--output", "Output format json|default");
        outputOpt.AddAlias("-o");
        outputOpt.SetDefaultValue("default");

        var importFileOption = new Option<string>(name: "--file", description: "Path to keys file to import")
        {
            IsRequired = true
        };

        var exportFileOption = new Option<string>(name: "--file", description: "Path to export keys")
        {
            IsRequired = true
        };

        var allowOption = new Option<bool>(name: "--allow", description: "Allow recreating node keys")
        {
            IsRequired = false
        };

        showCmd.AddOption(outputOpt);
        showCmd.SetHandler(ShowCommand, outputOpt);

        importCmd.AddOption(importFileOption);
        importCmd.SetHandler(ImportCommand, importFileOption);

        exportCmd.AddOption(exportFileOption);
        exportCmd.SetHandler(ExportCommand, exportFileOption);

        recreateCmd.AddOption(allowOption);
        recreateCmd.SetHandler(RecreateCommand, allowOption);

        return keysCmd;
    }

    public static void ShowCommand(string output)
    {
        var repository = new KeyRepository(_configuration);
        var privateKey = repository.GetPrivateKey();
        var publicKey = repository.GetPublicKey();

        if (output == "json")
        {
            var json = $$"""
            {
                "address": "{{publicKey.ToAddress()}}",
                "publicKey": "{{publicKey}}",
                "privateKey": "{{privateKey}}"
            }
            """;

            Console.WriteLine(json);
        }
        else
        {
            Console.WriteLine($"Address: {publicKey.ToAddress()}");
            Console.WriteLine($"Public key: {publicKey}");
            Console.WriteLine($"Private key: {privateKey}");
        }
    }

    public static void ImportCommand(string? file)
    {
        if (string.IsNullOrEmpty(file))
        {
            return;
        }

        var path = Path.GetFullPath(file);
        var repository = new KeyRepository(_configuration);
        repository.Import(path);

        Console.WriteLine($"Node keys imported from {path}");
    }

    public static void ExportCommand(string? file)
    {
        if (string.IsNullOrEmpty(file))
        {
            return;
        }

        var path = Path.GetFullPath(file);
        var repository = new KeyRepository(_configuration);
        repository.Export(path);

        Console.WriteLine($"Node keys exported to {path}");
    }

    public static void RecreateCommand(bool allow)
    {
        if (!allow)
        {
            Console.WriteLine("Recreating keys now allowed. Use '--allow' option");
            return;
        }

        var repository = new KeyRepository(_configuration);
        File.Delete(repository.StorePath);

        // Create and output freshly created keys
        ShowCommand("default");
    }
}
