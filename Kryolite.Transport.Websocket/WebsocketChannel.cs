using System.Buffers;
using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.WebSockets;
using System.Threading.Channels;
using Kryolite.ByteSerializer;
using Kryolite.Shared;

namespace Kryolite.Transport.Websocket;

public class WebsocketChannel : IDisposable
{
    public bool IsConnected => _ws?.State == WebSocketState.Open;

    public Channel<ArraySegment<byte>> Broadcasts => _duplex;
    public CancellationToken ConnectionToken => _cts.Token;

    private WebSocket? _ws;
    private readonly Uri _uri;
    private int _msgId;
    private CancellationTokenSource _cts;
    private byte[] _msgBuf = new byte[4];

    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly Channel<ArraySegment<byte>> _duplex = Channel.CreateUnbounded<ArraySegment<byte>>();
    private readonly ConcurrentDictionary<int, TaskCompletionSource<ArraySegment<byte>>> _requests = new();

    private static readonly byte[] _duplexMessage = [0];
    private static readonly byte[] _unaryRequest = [1];
    private static readonly byte[] _unaryReply = [2];

    private static readonly HttpClient _httpClient = new();

    public DateTime ConnectedSince { get; private set;}
    public long BytesSent { get; private set; }
    public long BytesReceived { get; private set; }
    public long MessagesSent { get; private set; }
    public long MessagesReceived { get; private set; }

    public WebsocketChannel(Uri uri, CancellationToken token)
    {
        _uri = uri;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(token);
    }

    public WebsocketChannel(Uri uri, WebSocket ws, CancellationToken token) : this (uri, token)
    {
        _ws = ws;
        _ = Receive(_cts.Token);
    }

    public static WebsocketChannel ForAddress(Uri uri, CancellationToken token)
        => new(uri, token);

    /// <summary>
    /// Test connection to node
    /// </summary>
    /// <returns></returns>
    public async ValueTask<(bool, string)> Ping()
    {
        try
        {
            if (IsConnected)
            {
                return (true, string.Empty);
            }

            var result = await _httpClient.GetAsync(new Uri(_uri, "?action=ping"), _cts.Token);

            return (result.IsSuccessStatusCode, result.ReasonPhrase ?? string.Empty);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>
    /// Get public key from node
    /// </summary>
    /// <returns></returns>
    public async ValueTask<(AuthResponse?, string)> GetPublicKey()
    {
        try
        {
            var result = await _httpClient.GetAsync(new Uri(_uri, "?action=whois"), _cts.Token);

            if (!result.IsSuccessStatusCode)
            {
                return (null, result.ReasonPhrase ?? string.Empty);
            }

            var bytes = await result.Content.ReadAsByteArrayAsync();

            return (Serializer.Deserialize<AuthResponse>(bytes), string.Empty);
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
    }

    /// <summary>
    /// Get peers from node
    /// </summary>
    /// <returns></returns>
    public async ValueTask<(NodeListResponse?, string)> GetPeers()
    {
        try
        {
            var result = await _httpClient.GetAsync(new Uri(_uri, "?action=peers"), _cts.Token);

            if (!result.IsSuccessStatusCode)
            {
                return (null, result.ReasonPhrase ?? string.Empty);
            }

            var bytes = await result.Content.ReadAsByteArrayAsync();

            return (Serializer.Deserialize<NodeListResponse>(bytes), string.Empty);
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
    }

    public bool Connect(AuthRequest authRequest, [NotNullWhen(false)] out string? reason)
    {
        reason = null;

        if (_ws is not null)
        {
            // If other code works as designed, we should not get into this if bracket
            TransportException.Throw();
        }

        try
        {
            var uriBuilder = new UriBuilder(_uri)
            {
                Scheme = _uri.Scheme == "https" ? "wss" : "ws"
            };
            
            var ws = new ClientWebSocket();
            ws.Options.SetRequestHeader("Authorization", Convert.ToBase64String(Serializer.Serialize(authRequest)));
            ws.ConnectAsync(uriBuilder.Uri, _cts.Token).Wait(TimeSpan.FromSeconds(30), _cts.Token);

            _ws = ws;
            ConnectedSince = DateTime.UtcNow;

            _ = Receive(_cts.Token);

            return true;
        }
        catch (TimeoutException tEx)
        {
            reason = tEx.Message;
            return false;
        }
        catch (OperationCanceledException cEx)
        {
            reason = cEx.Message;
            return false;
        }
        catch (WebSocketException wEx)
        {
            reason = wEx.Message;
            return false;
        }
    }

    public async Task SendDuplex(byte[] payload, CancellationToken token)
    {
        if (_ws is null)
        {
            TransportException.Throw();
        }

        if (!IsConnected)
        {
            TransportException.Throw();
        }

        await _lock.WaitAsync(token);

        await _ws.SendAsync(_duplexMessage, WebSocketMessageType.Binary, false, token);
        await _ws.SendAsync(payload, WebSocketMessageType.Binary, true, token);

        MessagesSent++;
        BytesSent += _duplexMessage.Length + payload.Length;

        _lock.Release();
    }

    public async Task<ArraySegment<byte>> SendUnary(ArrayBufferWriter<byte> writer, CancellationToken token)
    {
        if (_ws is null)
        {
            TransportException.Throw();
        }

        if (!IsConnected)
        {
            TransportException.Throw();
        }

        var msgId = Interlocked.Increment(ref _msgId);
        var tcs = new TaskCompletionSource<ArraySegment<byte>>();

        if (!_requests.TryAdd(msgId, tcs))
        {
            TransportException.Throw();
        }

        await _lock.WaitAsync(token);

        if (!BitConverter.TryWriteBytes(_msgBuf, msgId))
        {
            TransportException.Throw();
        }

        await _ws.SendAsync(_unaryRequest, WebSocketMessageType.Binary, false, token);
        await _ws.SendAsync(_msgBuf, WebSocketMessageType.Binary, false, token);
        await _ws.SendAsync(writer.WrittenMemory, WebSocketMessageType.Binary, true, token);

        MessagesSent++;
        BytesSent += _unaryRequest.Length + _msgBuf.Length + writer.WrittenCount;

        _lock.Release();

        return await tcs.Task
            .WaitAsync(TimeSpan.FromSeconds(30), token)
            .ContinueWith((result) =>
            {
                if (_requests.TryRemove(msgId, out var task))
                {
                    task.SetCanceled();
                }

                if (result.Exception is not null)
                {
                    throw result.Exception.InnerException!; // TimeoutException
                }

                return result.Result;
            });
    }

    public Task Disconnect(CancellationToken token)
    {
        if (!IsConnected)
        {
            return Task.CompletedTask;
        }

        try
        {
            return _ws?.CloseAsync(WebSocketCloseStatus.NormalClosure, null, token) ?? Task.CompletedTask;
        }
        catch (OperationCanceledException)
        {
            // The remote party closed the WebSocket connection without completing the close handshake
        }

        return Task.CompletedTask;
    }

    public T CreateClient<T>() where T : IWebsocketService<T> => ServiceResolver.CreateClient<T>(this);

    public void Dispose()
    {
        _duplex.Writer.Complete();
        _cts.Dispose();
        _ws?.Dispose();
    }

    private async Task Receive(CancellationToken token)
    {
        if (_ws is null)
        {
            TransportException.Throw();
        }

        try
        {
            var buffer = new byte[1024 * 32];

            while (!token.IsCancellationRequested && _ws.State == WebSocketState.Open)
            {
                var result = await _ws.ReceiveAsync(buffer, token);

                using var stream = new MemoryStream(result.Count);
                stream.Write(buffer.AsSpan(0, result.Count));

                // Read all data
                while (!result.EndOfMessage)
                {
                    result = await _ws.ReceiveAsync(buffer, token);
                    stream.Write(buffer.AsSpan(0, result.Count));
                }

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await Disconnect(token);
                    break;
                }

                var length = (int)stream.Length;
                var data = new ArraySegment<byte>(stream.GetBuffer()).Slice(0, length);

                _ = Task.Run(async () =>
                {
                    switch (data[0])
                    {
                        case 0:
                            await _duplex.Writer.WriteAsync(data.Slice(1), token);
                            break;
                        case 1:
                            var idBytes = data.Slice(1, 4);
                            var method = data[5];
                            var payload = data.Slice(6);

                            var service = ServiceResolver.Resolve(this);
                            var result = service.CallMethod(method, payload);

                            await _lock.WaitAsync(token);

                            await _ws.SendAsync(_unaryReply, WebSocketMessageType.Binary, false, token);
                            await _ws.SendAsync(idBytes, WebSocketMessageType.Binary, false, token);
                            await _ws.SendAsync(result, WebSocketMessageType.Binary, true, token);

                            _lock.Release();
                            break;
                        case 2:
                            var idBytes2 = data.Slice(1, 4);
                            var payload2 = data.Slice(5);

                            var msgId = BitConverter.ToInt32(idBytes2);

                            if (_requests.TryRemove(msgId, out var tcs))
                            {
                                tcs.SetResult(payload2);
                            }

                            break;
                        default:
                            TransportException.Throw();
                            break;
                    }
                }, token);

                MessagesReceived++;
                BytesReceived += length;
            }
        }
        finally
        {
            _cts.Cancel();
        }
    }
}
