using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Kryolite.ByteSerializer;
using Kryolite.Node.Network;
using Kryolite.Node.Repository;
using Kryolite.Shared;
using Kryolite.Shared.Dto;
using Kryolite.Transport.Websocket;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Kryolite.Node.API;

public static class WebsocketApi
{
    public static IEndpointRouteBuilder RegisterWebsocketEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapGet("/", WebsocketEndpoint);
        return builder;
    }

    private static async Task WebsocketEndpoint(HttpContext ctx, IServiceProvider sp, CancellationToken cancellationToken)
    {
        if (ctx.Request.Query.TryGetValue("action", out var action))
        {
            switch (action)
            {
                case "ping":
                    ctx.Response.StatusCode = StatusCodes.Status200OK;
                    break;
                case "whois":
                    {
                        using var scope2 = sp.CreateScope();
                        var authResponse = CreateAuthResponse(sp);

                        ctx.Response.StatusCode = StatusCodes.Status200OK;
                        await ctx.Response.BodyWriter.WriteAsync(Serializer.Serialize(authResponse), cancellationToken);
                    }
                    break;
                case "peers":
                    {
                        using var scope2 = sp.CreateScope();

                        var nodeTable2 = scope2.ServiceProvider.GetRequiredService<NodeTable>();
                        var peerList = new NodeListResponse(nodeTable2
                            .GetActiveNodes()
                            .Select(x => new NodeDto(x.PublicKey, x.Uri.ToString(), x.FirstSeen, x.LastSeen, x.Version))
                            .ToList()
                        );

                        ctx.Response.StatusCode = StatusCodes.Status200OK;
                        await ctx.Response.BodyWriter.WriteAsync(Serializer.Serialize(peerList), cancellationToken);
                    }
                    break;
                default:
                    ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
                    break;
            }

            return;
        }

        if (!ctx.WebSockets.IsWebSocketRequest)
        {
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var authorization = ctx.Request.Headers.Authorization.ToString();

        if (authorization is null)
        {
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var authRequest = Serializer.Deserialize<AuthRequest>(Convert.FromBase64String(authorization));

        if (!Authorize(ctx, authRequest, sp, out var uri, out var publicKey))
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var ws = await ctx.WebSockets.AcceptWebSocketAsync();
        var lifetime = sp.GetRequiredService<IHostApplicationLifetime>();

        var channel = new WebsocketChannel(uri, ws, lifetime.ApplicationStopping);

        using var scope = sp.CreateScope();
        var connectionManager = scope.ServiceProvider.GetRequiredService<IConnectionManager>();

        BroadcastManager.Broadcast(new NodeBroadcast(authRequest, uri.ToString()));

        await connectionManager.StartListening(uri, authRequest.PublicKey, channel, authRequest.Version);
    }

    private static bool Authorize(HttpContext ctx, AuthRequest authRequest, IServiceProvider sp, [NotNullWhen(true)] out Uri? uri, [NotNullWhen(true)] out PublicKey? publicKey)
    {
        using var scope = sp.CreateScope();

        var nodeTable = scope.ServiceProvider.GetRequiredService<NodeTable>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<NodeService>>();
        var keyRepository = scope.ServiceProvider.GetRequiredService<IKeyRepository>();
        var nodeKey = keyRepository.GetPublicKey();

        uri = null;
        publicKey = null;

        if (nodeKey == authRequest.PublicKey)
        {
            return false;
        }

        if (!authRequest.Verify())
        {
            logger.LogDebug("AuthRequest verification failed");
            return false;
        }

        if (authRequest.NetworkName != Constant.NETWORK_NAME)
        {
            logger.LogDebug("Invalid network name");
            return false;
        }

        if (authRequest.ApiLevel < Constant.MIN_API_LEVEL)
        {
            logger.LogDebug("Too low apilevel");
            return false;
        }

        if (!string.IsNullOrEmpty(authRequest.PublicUri))
        {
            uri = new Uri(authRequest.PublicUri);
        }
        else
        {
            var builder = new UriBuilder("http", ctx.Connection.RemoteIpAddress!.ToString(), authRequest.Port);
            uri = builder.Uri;
        }

        publicKey = authRequest.PublicKey;

        return true;
    }

    private static AuthResponse CreateAuthResponse(IServiceProvider sp)
    {
        using var scope = sp.CreateScope();

        var keyRepo = scope.ServiceProvider.GetRequiredService<IKeyRepository>();
        var version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? string.Empty;
        var authResponse = new AuthResponse(keyRepo.GetPublicKey(), Random.Shared.NextInt64(), version);

        authResponse.Sign(keyRepo.GetPrivateKey());

        return authResponse;
    }
}
