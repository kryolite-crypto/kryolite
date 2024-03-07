using System.Collections.Concurrent;
using DnsClient;
using Grpc.Core;
using Grpc.Net.Client;
using Kryolite.Grpc.NodeService;
using Kryolite.Shared.Dto;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ServiceModel.Grpc.Client;

namespace Kryolite.Node.Network;

public class DiscoveryManager : BackgroundService
{
    private readonly NodeTable _nodeTable;
    private readonly IClientFactory _clientFactory;
    private readonly ILookupClient _lookupClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DiscoveryManager> _logger;
    private readonly TimeSpan _timeout;

    public DiscoveryManager(NodeTable nodeTable, IClientFactory clientFactory, ILookupClient lookupClient, IConfiguration configuration, ILogger<DiscoveryManager> logger)
    {
        _nodeTable = nodeTable;
        _clientFactory = clientFactory;
        _lookupClient = lookupClient;
        _configuration = configuration;
        _logger = logger;
        _timeout = TimeSpan.FromSeconds(configuration.GetValue<int>("timeout"));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DiscMan       [UP]");
        
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));

            do
            {
                try
                {
                    await LoadSeedNodes(stoppingToken);
                    await DoInitialDiscovery(stoppingToken);
                }
                catch (RpcException ex)
                {
                    _logger.LogInformation(ex.Message);
                    _logger.LogInformation("Unable to contact any nodes, retrying in 15 seconds");
                }

                break;
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // stopping, do nothing
        }
    }

    private async Task LoadSeedNodes(CancellationToken stoppingToken)
    {
        var discovery = _configuration.GetValue<bool>("discovery");
        var seednode = _configuration.GetValue<string?>("seednode");
        var nodes = new List<string>();

        if (!string.IsNullOrEmpty(seednode))
        {
            nodes.Add(seednode);
        }

        if (discovery)
        {
            _logger.LogInformation("Resolving peers from testnet.kryolite.io");

            var result = await _lookupClient.QueryAsync("testnet.kryolite.io", QueryType.TXT, cancellationToken: stoppingToken);

            if (result.HasError)
            {
                throw new InvalidOperationException(result.ErrorMessage);
            }

            foreach (var txtRecord in result.Answers.TxtRecords().SelectMany(x => x.Text))
            {
                nodes.Add(txtRecord);
            }
        }

        foreach (var url in nodes)
        {
            if (!UriFormatter.TryParse(url, out var uri))
            {
                _logger.LogInformation("Failed to parse '{node}' as Uri", uri);
                continue;
            }

            var channel = GrpcChannel.ForAddress(uri);

            var result = await channel.ConnectAsync(stoppingToken).WithTimeout(_timeout, stoppingToken);

            if (!result)
            {
                _logger.LogInformation("Failed to download nodes from seed {hostname}", url);
                continue;
            }

            var client = _clientFactory.CreateClient<INodeService>(channel);
            var publicKey = client.GetPublicKey();

            await channel.ShutdownAsync();

            _nodeTable.AddNode(publicKey, uri, channel);
        }
    }

    private async Task DoInitialDiscovery(CancellationToken stoppingToken)
    {
        if (_nodeTable.GetNodesCount() == 0)
        {
            _logger.LogError("Unable to perform node discovery. Make sure DNS requests to testnet.kryolite.io are not blocked or set your own seed node with option '--seednode http://127.0.0.1:5000'");
            return;
        }

        var activeNodes = _nodeTable.GetActiveNodes();
        var downloaded = new ConcurrentBag<NodeDto>();

        await Parallel.ForEachAsync(activeNodes, async (node, ct) => 
        {
            _logger.LogInformation("Downloading nodes from {node}", node.Uri);

            var result = await node.Channel.ConnectAsync(stoppingToken).WithTimeout(_timeout, stoppingToken);

            if (!result)
            {
                _logger.LogInformation("Failed to download nodes from {hostname}", node.Uri.ToHostname());
                return;
            }

            var client = _clientFactory.CreateClient<INodeService>(node.Channel);
            var nodes = client.GetPeers();

            foreach (var peer in nodes.Nodes)
            {
                downloaded.Add(peer);
            }
        });

        var distinct = downloaded.Distinct().ToList();

        _logger.LogInformation("Downloaded {distinct} nodes", distinct.Count);

        Parallel.ForEach(distinct, nodeDto =>
        {
            _nodeTable.AddNode(nodeDto.PublicKey, new Uri(nodeDto.Url));
        });
    }
}
