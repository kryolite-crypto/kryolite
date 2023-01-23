using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.LoadBalancing;
using Yarp.ReverseProxy.Transforms;

namespace Kryolite.Node;
    
    public class ProxyConfigProvider : IProxyConfigProvider
    {
        private MemoryConfig _config;

        public ProxyConfigProvider(IConfiguration configuration, IMeshNetwork meshNetwork)
        {
            var wsRouteConfig = new RouteConfig
            {
                RouteId = "ws",
                ClusterId = "ws",
                Match = new RouteMatch
                {
                    Path = "{**catch-all}",
                    Headers = new List<RouteHeader>
                    {
                        new RouteHeader
                        {
                            Name = "kryo-client-id",
                            Mode = HeaderMatchMode.Exists
                        }
                    }
                }
            };

            wsRouteConfig = wsRouteConfig.WithTransformXForwarded(
                xDefault: ForwardedTransformActions.Off,
                xFor: ForwardedTransformActions.Append
            );

            var routeConfigs = new[] {
                wsRouteConfig
            };

            var clusterConfigs = new[]
            {
                new ClusterConfig
                {
                    ClusterId = "ws",
                    LoadBalancingPolicy = LoadBalancingPolicies.RoundRobin,
                    Destinations = new Dictionary<string, DestinationConfig>
                    {
                        { "destination1", new DestinationConfig { Address = $"ws://127.0.0.1:{meshNetwork.GetLocalPort()}/" } }
                    }
                }
            };

            _config = new MemoryConfig(routeConfigs, clusterConfigs);
        }

        public IProxyConfig GetConfig() => _config;

        public void Update(IReadOnlyList<RouteConfig> routes, IReadOnlyList<ClusterConfig> clusters)
        {
            var oldConfig = _config;
            _config = new MemoryConfig(routes, clusters);
            oldConfig.SignalChange();
        }

        private class MemoryConfig : IProxyConfig
        {
            private readonly CancellationTokenSource _cts = new CancellationTokenSource();

            public MemoryConfig(IReadOnlyList<RouteConfig> routes, IReadOnlyList<ClusterConfig> clusters)
            {
                Routes = routes;
                Clusters = clusters;
                ChangeToken = new CancellationChangeToken(_cts.Token);
            }

            public IReadOnlyList<RouteConfig> Routes { get; }

            public IReadOnlyList<ClusterConfig> Clusters { get; }

            public IChangeToken ChangeToken { get; }

            internal void SignalChange()
            {
                _cts.Cancel();
            }
        }
    }
