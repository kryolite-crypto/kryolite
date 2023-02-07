using System.Net.WebSockets;
using MessagePack;

namespace Kryolite.Node;

public enum ConnectionType
{
    IN,
    OUT
}

public class Peer
{
    public ulong Id { get; private init; }
    public Uri Uri { get; private init; }
    public ConnectionType ConnectionType { get; private init; }

    public DateTime LastSeen { get; set; }
    public DateTime ConnectedSince { get; set; }
    public bool IsReachable { get; set; }


    private WebSocket Socket { get; }
    private SemaphoreSlim _lock = new SemaphoreSlim(1);

    public Peer(WebSocket socket, ulong id, Uri uri, ConnectionType connectionType, bool isReacable)
    {
        Socket = socket ?? throw new ArgumentNullException(nameof(socket));
        Id = id;
        Uri = uri ?? throw new ArgumentNullException(nameof(uri));
        ConnectionType = connectionType;
        IsReachable = isReacable;

        ConnectedSince = DateTime.UtcNow;
        LastSeen = DateTime.UtcNow;
    }

    public async Task SendAsync(IPacket packet, CancellationToken? token = null)
    {
        try
        {
            var msg = new Message((uint)Random.Shared.NextInt64(), packet);
            var bytes = MessagePackSerializer.Serialize(msg, MeshNetwork.lz4Options);

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
                await Socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "ByeBye!", token ?? CancellationToken.None);
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
}