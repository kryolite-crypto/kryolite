using System.Net;
using System.Net.WebSockets;
using Kryolite.Shared;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kryolite.Node.API;

public static class AuthorizeApi
{
    public static IEndpointRouteBuilder RegisterAuthApi(this IEndpointRouteBuilder builder)
    {
        // for let's encrypt
        builder.MapGet(".well-known/{**catch-all}", HandleLetsEncryptHandshake);
        builder.MapGet("authorize", AuthorizeAndAcceptConnection);
        builder.MapGet("ping2", HandlePing);

        return builder;
    }

    private static Task<string> HandleLetsEncryptHandshake() => Task.Run(() =>
    {
        return "OK";
    });

    private static Task<string> HandlePing() => Task.Run(() =>
    {
        return "PONG";
    });

    private static async Task AuthorizeAndAcceptConnection(HttpContext context, IServiceProvider sp)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        using var webSocket = await context.WebSockets.AcceptWebSocketAsync();

        using var scope = sp.CreateScope();
        var logger = sp.GetRequiredService<ILogger<INetworkManager>>();
        var networkManager = scope.ServiceProvider.GetRequiredService<INetworkManager>();

        if (!int.TryParse(context.Request.Headers["kryo-apilevel"], out var apilevel))
        {
            logger.LogDebug("Received connection without api level, forcing disconnect...");
            await webSocket.CloseAsync(WebSocketCloseStatus.ProtocolError, "API_LEVEL_NOT_SET", CancellationToken.None);
            return;
        }

        if (apilevel < Constant.MIN_API_LEVEL)
        {
            logger.LogDebug("Incoming connection apilevel not supported ({apilevel}), forcing disconnect...", apilevel);
            await webSocket.CloseAsync(WebSocketCloseStatus.ProtocolError, "UNSUPPORTED_API_LEVEL", CancellationToken.None);
            return;
        }

        if (string.IsNullOrEmpty(context.Request.Headers["kryo-client-id"]))
        {
            logger.LogDebug("Received connection without client-id, forcing disconnect...");
            await webSocket.CloseAsync(WebSocketCloseStatus.ProtocolError, "CLIENT_ID_NOT_SET", CancellationToken.None);
            return;
        }

        if (!ulong.TryParse(context.Request.Headers["kryo-client-id"], out var clientId))
        {
            logger.LogDebug("Received connection with invalid client-id, forcing disconnect...");
            await webSocket.CloseAsync(WebSocketCloseStatus.ProtocolError, "INVALID_CLIENT_ID", CancellationToken.None);
            return;
        }

        if (networkManager.IsBanned(clientId))
        {
            logger.LogDebug("Received connection from banned node {clientId}, forcing disconnect...", clientId);
            await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "BANNED_CLIENT", CancellationToken.None);
            return;
        }

        if (context.Request.Headers["kryo-network"] != Constant.NETWORK_NAME)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug("Wrong network: '{kryoNetwork}'", context.Request.Headers["kryo-network"].ToString());
            }

            await webSocket.CloseAsync(WebSocketCloseStatus.ProtocolError, "WRONG_NETWORK", CancellationToken.None);
            return;
        }

        var mesh = sp.GetRequiredService<IMeshNetwork>();

        if (clientId == mesh.GetServerId())
        {
            logger.LogDebug("Self connection, disconnecting client...");
            await webSocket.CloseAsync(WebSocketCloseStatus.ProtocolError, "SELF_CONNECTION", CancellationToken.None);
            return;
        }

        var url = context.Request.Headers["kryo-connect-to-url"];

        if (!string.IsNullOrEmpty(url) && Uri.TryCreate(url, new UriCreationOptions(), out var uri))
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogInformation("Received connection from {url}", url.ToString());
            }

            var success = await Connection.TestConnectionAsync(uri);

            if (!success)
            {
                logger.LogInformation("Force disconnect {uri}, reason: url not reachable", uri);
                await webSocket.CloseAsync(WebSocketCloseStatus.ProtocolError, "URL_NOT_REACHABLE", CancellationToken.None);

                return;
            }

            var urlPeer = new Peer(webSocket, clientId, uri, ConnectionType.IN, true, apilevel);
            await mesh.AddSocketAsync(webSocket, urlPeer);

            return;
        }

        IPAddress? address = null;
        var forwardedFor = context.Request.Headers["X-Forwarded-For"].ToString();

        logger.LogDebug("X-Forwarded-For = " + forwardedFor);

        if (!string.IsNullOrEmpty(forwardedFor))
        {
            address = forwardedFor
                .Split(",")
                .Select(x => IPAddress.Parse(x.Trim()))
                .Reverse()
                .Where(x => x.IsPublic())
                .LastOrDefault();

            if (address == null)
            {
                address = forwardedFor
                    .Split(",")
                    .Select(x => IPAddress.Parse(x.Trim()))
                    .Reverse()
                    .LastOrDefault();
            }
        }

        address ??= context.Request.HttpContext.Connection.RemoteIpAddress;

        logger.LogInformation($"Received connection from {address}");

        List<Uri> hosts = new List<Uri>();

        var ports = context.Request.Headers["kryo-connect-to-ports"].ToString();

        foreach (var portStr in ports.Split(','))
        {
            if (int.TryParse(portStr, out var port))
            {
                var builder = new UriBuilder()
                {
                    Host = address!.ToString(),
                    Port = port
                };

                hosts.Add(builder.Uri);
            }
        }

        Uri? bestUri = null;
        bool isReachable = false;

        foreach (var host in hosts)
        {
            try
            {
                var success = await Connection.TestConnectionAsync(host);

                if (!success)
                {
                    logger.LogDebug($"Failed to open connection to {host}, skipping host...");
                    continue;
                }

                bestUri = host;
                isReachable = true;
                break;
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, $"Connection failure: {host}");
            }
        }

        if (bestUri == null)
        {
            bestUri = hosts.LastOrDefault();
        }

        if (bestUri == null)
        {
            bestUri = new UriBuilder
            {
                Host = context.Request.HttpContext.Connection.RemoteIpAddress?.ToString(),
                Port = context.Request.HttpContext.Connection.RemotePort
            }.Uri;
        }

        var peer = new Peer(webSocket, clientId, bestUri, ConnectionType.IN, isReachable, apilevel);

        await mesh.AddSocketAsync(webSocket, peer);
    }
}
