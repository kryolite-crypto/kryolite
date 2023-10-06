using System.Collections.Concurrent;
using System.Net.WebSockets;
using Kryolite.Shared;
using Kryolite.Shared.Dto;
using MessagePack;

namespace Kryolite.Node;

public enum ConnectionType
{
    IN,
    OUT
}

public class Peer : IDisposable
{
    public ulong ClientId { get; private init; }
    public Uri Uri { get; private init; }
    public ConnectionType ConnectionType { get; private init; }

    public DateTime LastSeen { get; set; }
    public DateTime ConnectedSince { get; set; }
    public bool IsReachable { get; set; }
    public int ApiLevel { get; private init; }
    public bool IsSyncInProgress { get; set; }
    public ConcurrentDictionary<ulong, TaskCompletionSource<Reply>> ReplyQueue = new();

    private WebSocket Socket { get; }
    private SemaphoreSlim _lock = new SemaphoreSlim(1);

    public Peer(WebSocket socket, ulong id, Uri uri, ConnectionType connectionType, bool isReacable, int apiLevel)
    {
        Socket = socket ?? throw new ArgumentNullException(nameof(socket));
        ClientId = id;
        Uri = uri ?? throw new ArgumentNullException(nameof(uri));
        ConnectionType = connectionType;
        IsReachable = isReacable;

        ConnectedSince = DateTime.UtcNow;
        LastSeen = DateTime.UtcNow;
        ApiLevel = apiLevel;
    }

    public async Task<Reply?> PostAsync(IPacket packet, CancellationToken? token = null)
    {
        var msg = new Message(packet);

        try
        {
            var bytes = MessagePackSerializer.Serialize<IMessage>(msg, MeshNetwork.lz4Options);
            
            token ??= CancellationToken.None;

            var tcs = new TaskCompletionSource<Reply>();
            ReplyQueue.TryAdd(msg.Id, tcs);

            await SendAsync(bytes);

            return await tcs.Task.WithTimeout(TimeSpan.FromSeconds(30));
        }
        catch (TimeoutException)
        {
            // WithTimeout timeouts
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
            return null;
        }
        finally
        {
            ReplyQueue.TryRemove(msg.Id, out _);
        }
    }

    public async Task ReplyAsync(ulong replyTo, IPacket packet, CancellationToken? token = null)
    {
        try
        {
            var msg = new Reply(replyTo, packet);
            var bytes = MessagePackSerializer.Serialize<IMessage>(msg, MeshNetwork.lz4Options);

            await SendAsync(bytes,token ?? CancellationToken.None);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
        }
    }

    public async Task SendAsync(IPacket packet, CancellationToken? token = null)
    {
        try
        {
            var msg = new Message(packet);
            var bytes = MessagePackSerializer.Serialize<IMessage>(msg, MeshNetwork.lz4Options);

            await SendAsync(bytes,token ?? CancellationToken.None);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
        }
    }

    public async Task SendAsync(ulong id, IPacket packet, CancellationToken? token = null)
    {
        try
        {
            var msg = new Message(id, packet);
            var bytes = MessagePackSerializer.Serialize<IMessage>(msg, MeshNetwork.lz4Options);

            await SendAsync(bytes,token ?? CancellationToken.None);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
        }
    }

    public async Task SendAsync(byte[] bytes, CancellationToken? token = null)
    {
        try
        {
            await _lock.WaitAsync();

            if (Socket.State == WebSocketState.Open || Socket.State == WebSocketState.CloseSent)
            {
                await Socket.SendAsync(bytes, WebSocketMessageType.Binary, true, token ?? CancellationToken.None);
            }
        }
        catch (ObjectDisposedException)
        {
            // already disconnected and disposed
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task DisconnectAsync(CancellationToken? token = null)
    {
        try
        {
            await _lock.WaitAsync();

            if (Socket.State == WebSocketState.Open || Socket.State == WebSocketState.CloseReceived || Socket.State == WebSocketState.CloseSent)
            {
                await Socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Disconnected", token ?? CancellationToken.None);
            }
        }
        catch (ObjectDisposedException)
        {
            // already disconnected and disposed
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Dispose()
    {
        Socket.Dispose();
    }
}
