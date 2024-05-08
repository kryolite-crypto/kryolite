using System.Collections.Concurrent;
using DnsClient;
using Kryolite.Grpc.NodeService;
using Kryolite.Shared.Dto;
using Kryolite.Transport.Websocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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
                catch (Exception ex)
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

    /// <summary>
    /// Download seed nodes, verify their public key and add them to NodeTable
    /// </summary>
    /// <param name="stoppingToken"></param>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException"></exception>
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

            using var channel = WebsocketChannel.ForAddress(uri, stoppingToken);

            var (authResponse, error) = await channel.GetPublicKey();

            if (authResponse is null)
            {
                _logger.LogInformation("Failed to request node public key {hostname}: {error}", url, error);
                continue;
            }

            if (!authResponse.Verify())
            {
                _logger.LogInformation("Node signature failed {hostname}", url);
                continue;
            }

            _nodeTable.AddNode(authResponse.PublicKey, uri, authResponse.Version);
        }
    }

    /// <summary>
    /// Download peers from known nodes and add them to NodeTable
    /// </summary>
    /// <param name="stoppingToken"></param>
    /// <returns></returns>
    private async Task DoInitialDiscovery(CancellationToken stoppingToken)
    {
        if (_nodeTable.GetNodesCount() == 0)
        {
            var discovery = _configuration.GetValue<bool>("discovery");

            if (discovery)
            {
                _logger.LogError("Unable to perform node discovery. Make sure DNS requests to testnet.kryolite.io are not blocked or set your own seed node with option '--seednode http://127.0.0.1:5000'");
            }
            else
            {
                _logger.LogWarning("Discovery is disabled, manually add seed node with option '--seednode'");
            }

            return;
        }

        var activeNodes = _nodeTable.GetActiveNodes();
        var downloaded = new ConcurrentBag<NodeDto>();

        await Parallel.ForEachAsync(activeNodes, async (node, ct) =>
        {
            _logger.LogInformation("Downloading nodes from {node}", node.Uri);

            using var channel = WebsocketChannel.ForAddress(node.Uri, stoppingToken);

            var (peerList, error) = await channel.GetPeers();

            if (peerList is null)
            {
                _logger.LogInformation("Failed to download nodes from {hostname}: {error}", node.Uri.ToHostname(), error);
                return;
            }

            foreach (var peer in peerList.Nodes)
            {
                downloaded.Add(peer);
            }
        });

        var distinct = downloaded.Distinct().ToList();

        _logger.LogInformation("Downloaded {distinct} nodes", distinct.Count);

        Parallel.ForEach(distinct, nodeDto =>
        {
            _nodeTable.AddNode(nodeDto.PublicKey, new Uri(nodeDto.Url), nodeDto.Version);
        });
    }
}
